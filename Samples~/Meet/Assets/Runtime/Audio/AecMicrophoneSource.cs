using System;
using System.Collections;
using LiveKit;
using UnityEngine;

/// <summary>
/// Microphone capture source that runs AEC3 over the captured PCM before publishing it.
///
/// The SDK's <c>MicrophoneSource</c> is sealed and its <c>AudioProbe</c> and
/// <c>MonoBehaviourContext</c> are internal, so the capture path is re-implemented here. Its
/// behaviours are load-bearing and preserved: clear-after-invocation (so the microphone is not
/// played back locally), the duplicate-component guard, the <c>Microphone.GetPosition</c>
/// readiness poll, and the pause/resume stop-restart cycle.
///
/// The only behavioural difference is that <see cref="AudioRead"/> carries APM-processed 10 ms
/// chunks instead of raw DSP blocks. When the APM is unavailable the raw blocks pass straight
/// through, so publishing never fails because of the canceller.
/// </summary>
internal sealed class AecMicrophoneSource : RtcAudioSource
{
    private readonly GameObject _sourceObject;
    private readonly string _deviceName;
    private readonly AecMicrophoneHost _host;
    private readonly AcousticEchoCanceller _canceller;

    public override event Action<float[], int, int> AudioRead;

    private bool _disposed;
    private bool _started;

    public bool EchoCancellationActive => _canceller != null;

    private AecMicrophoneSource(
        string deviceName,
        GameObject sourceObject,
        AecMicrophoneHost host,
        AcousticEchoCanceller canceller)
        : base(RtcAudioSourceType.AudioSourceMicrophone)
    {
        _deviceName = deviceName;
        _sourceObject = sourceObject;
        _host = host;
        _canceller = canceller;

        if (_canceller != null) _canceller.CaptureProcessed += OnProcessedAudio;
    }

    /// <summary>
    /// Builds the source. The base constructor configures the native source from Unity's current
    /// output configuration, which is also the format <c>OnAudioFilterRead</c> delivers, so the
    /// published track's metadata matches the capture geometry without further alignment.
    /// </summary>
    /// <param name="deviceName">One of <see cref="Microphone.devices"/>.</param>
    /// <param name="sourceObject">GameObject that hosts the AudioSource, probe and coroutine
    /// runner. Must stay alive for the source's lifetime.</param>
    public static AecMicrophoneSource Create(string deviceName, GameObject sourceObject)
    {
        if (sourceObject == null) throw new ArgumentNullException(nameof(sourceObject));

        var host = sourceObject.GetComponent<AecMicrophoneHost>();
        if (host == null) host = sourceObject.AddComponent<AecMicrophoneHost>();

        return new AecMicrophoneSource(deviceName, sourceObject, host, AcousticEchoCanceller.TryCreate());
    }

    public override void Start()
    {
        base.Start();
        if (_started) return;

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            throw new InvalidOperationException("Microphone access not authorized");

        _host.Paused += OnApplicationPause;
        _canceller?.Start();
        RunCoroutine(StartMicrophone());

        _started = true;
    }

    public override void Stop()
    {
        base.Stop();
        RunCoroutine(StopMicrophone());
        if (_host != null) _host.Paused -= OnApplicationPause;
        _canceller?.Stop();
        _started = false;
    }

    private IEnumerator StartMicrophone()
    {
        if (_sourceObject == null)
        {
            Debug.LogError("[AEC] microphone GameObject is null, cannot start");
            yield break;
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            Debug.LogError("[AEC] microphone authorization lost");
            yield break;
        }

        AudioClip clip = null;
        try
        {
            clip = Microphone.Start(
                _deviceName,
                loop: true,
                lengthSec: 1,
                frequency: SupportedCaptureFrequency());
        }
        catch (Exception e)
        {
            Debug.LogError($"[AEC] exception starting microphone: {e.Message}");
            yield break;
        }

        if (clip == null)
        {
            Debug.LogError("[AEC] Microphone.Start returned null, audio session may not be ready");
            yield break;
        }

        // Unity's Destroy is deferred, so a resume can land here while the previous pair is still
        // alive. Duplicates would double every captured block into the APM.
        var existingSource = _sourceObject.GetComponent<AudioSource>();
        if (existingSource != null) UnityEngine.Object.DestroyImmediate(existingSource);

        var existingProbe = _sourceObject.GetComponent<AecAudioProbe>();
        if (existingProbe != null)
        {
            existingProbe.AudioRead -= OnCapturedAudio;
            UnityEngine.Object.DestroyImmediate(existingProbe);
        }

        var source = _sourceObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;

        var probe = _sourceObject.AddComponent<AecAudioProbe>();
        probe.ClearAfterInvocation();
        probe.AudioRead += OnCapturedAudio;

        const float timeout = 2f;
        var elapsed = 0f;
        while (Microphone.GetPosition(_deviceName) <= 0 && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.05f);
            elapsed += 0.05f;
        }

        if (Microphone.GetPosition(_deviceName) <= 0)
        {
            Debug.LogError($"[AEC] microphone did not start producing data after {timeout}s");
            yield break;
        }

        source.Play();
        Debug.Log($"[AEC] microphone '{_deviceName}' started at {clip.frequency}Hz, echo cancellation={EchoCancellationActive}");
    }

    // The requested rate is Unity's output rate, which the device may not accept as a capture
    // rate. Clamping keeps Microphone.Start working; the DSP graph resamples the clip anyway, so
    // OnAudioFilterRead still delivers the output rate either way.
    private int SupportedCaptureFrequency()
    {
        var requested = AudioSettings.outputSampleRate;
        Microphone.GetDeviceCaps(_deviceName, out var min, out var max);

        // Unity reports 0/0 when the device accepts any frequency.
        if (min == 0 && max == 0) return requested;

        var clamped = Mathf.Clamp(requested, min, max);
        if (clamped != requested)
            Debug.Log($"[AEC] capture rate clamped {requested} -> {clamped} (device caps {min}-{max})");

        return clamped;
    }

    private IEnumerator StopMicrophone()
    {
        if (Microphone.IsRecording(_deviceName))
            Microphone.End(_deviceName);

        if (_sourceObject != null)
        {
            var probe = _sourceObject.GetComponent<AecAudioProbe>();
            if (probe != null)
            {
                probe.AudioRead -= OnCapturedAudio;
                UnityEngine.Object.Destroy(probe);
            }

            var source = _sourceObject.GetComponent<AudioSource>();
            if (source != null)
                UnityEngine.Object.Destroy(source);
        }

        Debug.Log($"[AEC] microphone '{_deviceName}' stopped");
        yield return null;
    }

    // Unity audio thread. A block the canceller cannot take is published unchanged rather than
    // dropped — an un-cancelled participant beats a silent one.
    private void OnCapturedAudio(float[] data, int channels, int sampleRate)
    {
        if (_canceller != null && _canceller.TryPushCapture(data, channels, sampleRate)) return;

        AudioRead?.Invoke(data, channels, sampleRate);
    }

    // Unity audio thread, via the capture pump.
    private void OnProcessedAudio(float[] data, int channels, int sampleRate)
    {
        AudioRead?.Invoke(data, channels, sampleRate);
    }

    private void OnApplicationPause(bool pause)
    {
        if (!_started) return;

        if (pause)
        {
            // Backgrounded, release the audio resources — leaving them open trips
            // AVAudioSession interruption errors (FigCaptureSourceRemote -17281).
            _canceller?.Stop();
            RunCoroutine(StopMicrophone());
        }
        else
        {
            RunCoroutine(RestartMicrophone());
        }
    }

    private IEnumerator RestartMicrophone()
    {
        yield return StopMicrophone();

        // After a resume the iOS audio session needs time to recover from interruption. Poll for
        // actual readiness instead of guessing a delay.
        yield return WaitForMicrophoneReady();

        _canceller?.Start();
        yield return StartMicrophone();
    }

    private IEnumerator WaitForMicrophoneReady()
    {
        const float timeout = 2f;
        var elapsed = 0f;

        while (Microphone.devices.Length == 0 && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.05f);
            elapsed += 0.05f;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError($"[AEC] microphone devices not available after {timeout}s");
            yield break;
        }

        yield return null;
    }

    // The host is a component on the (caller-owned) microphone GameObject. If that object is
    // already gone — scene unload, app quit — drain the coroutine synchronously so Microphone.End
    // and the component cleanup still run, as the SDK's MonoBehaviourContext does.
    private void RunCoroutine(IEnumerator coroutine)
    {
        if (_host != null)
        {
            _host.StartCoroutine(coroutine);
            return;
        }

        while (coroutine.MoveNext())
        {
            if (coroutine.Current is IEnumerator nested)
                RunCoroutine(nested);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing) Stop();
        _disposed = true;

        if (_canceller != null)
        {
            _canceller.CaptureProcessed -= OnProcessedAudio;
            _canceller.Dispose();
        }

        base.Dispose(disposing);
    }

    ~AecMicrophoneSource()
    {
        Dispose(false);
    }
}
