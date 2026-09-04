using System;
using System.Diagnostics;
using System.Threading;
using LiveKit;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runs libwebrtc's AEC3 over the local microphone capture, using the decoded remote audio as
/// the echo reference.
///
/// The loudspeaker plays the remote audio, the microphone re-captures it, and the published
/// track would carry it back to everyone. Unity's <c>Microphone</c> path has no echo canceller
/// anywhere (capture never goes through a platform audio device module), so cancellation is
/// done here against the two streams already available in managed code: the FFI render frames
/// (far end, via <see cref="FfiFrameObserver"/>) and <c>OnAudioFilterRead</c> (near end).
///
/// Threading: <see cref="TryPushCapture"/> runs on the Unity audio thread and
/// <see cref="OnFarEndFrame"/> on the FFI callback thread. Each owns its own pump, and
/// libwebrtc's APM is built for exactly that capture/render thread split. Nothing here touches
/// a Unity API from those threads; diagnostics use <see cref="Stopwatch"/>, not
/// <c>UnityEngine.Time</c>.
///
/// Limitation: the far-end tap is not filtered by stream, so it assumes a SINGLE remote audio
/// stream. With several remote speakers the reference becomes the interleaving of all their
/// frames and AEC3 will not converge. The remote audio must also play through Unity
/// (<c>AudioStream</c>) — in PlatformAudio mode no FFI audio streams exist and nothing arrives.
/// </summary>
internal sealed class AcousticEchoCanceller : IDisposable
{
    private const int DiagnosticIntervalMs = 5000;
    private const int SeedIntervalMs = 2000;
    private const int SeedChangeThresholdMs = 5;

    private readonly AudioProcessingModule _apm;
    private readonly ApmChunkPump _capturePump;
    private readonly ApmChunkPump _renderPump;
    private readonly Stopwatch _clock = new Stopwatch();

    private long _nextDiagnosticMs;
    private long _nextSeedMs;
    private int _seededDelayMs = -1;
    private int _farEndFrames;
    private int _unsupportedRateWarned;
    private bool _subscribed;
    private bool _disposed;

    /// <summary>Processed 10 ms capture chunks, raised on the Unity audio thread.</summary>
    public event ProcessedChunkHandler CaptureProcessed;

    private AcousticEchoCanceller(AudioProcessingModule apm)
    {
        _apm = apm;
        _capturePump = new ApmChunkPump(_apm.ProcessStream, RaiseCaptureProcessed);
        _renderPump = new ApmChunkPump(_apm.ProcessReverseStream);
    }

    /// <summary>
    /// Creates the canceller, or returns null when the FFI cannot hand out an APM handle — the
    /// caller then publishes the unprocessed microphone rather than failing to publish at all.
    /// </summary>
    public static AcousticEchoCanceller TryCreate()
    {
        try
        {
            var apm = new AudioProcessingModule(
                echoCancellerEnabled: true,
                gainControllerEnabled: true,
                highPassFilterEnabled: true,
                noiseSuppressionEnabled: true);

            Debug.Log($"[AEC] APM created handle={apm.Handle}");
            return new AcousticEchoCanceller(apm);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AEC] APM unavailable, publishing microphone unprocessed: {e.Message}");
            return null;
        }
    }

    public void Start()
    {
        if (_disposed || _subscribed) return;

        _capturePump.Reset();
        _renderPump.Reset();
        _clock.Restart();
        _nextDiagnosticMs = DiagnosticIntervalMs;
        _nextSeedMs = SeedIntervalMs;

        // Forget the delay seeded before the last Stop(). A resume restarts the whole audio path,
        // so the previous value describes an acoustic path that no longer exists — and carrying it
        // over lets the SeedChangeThresholdMs guard in SeedStreamDelay silently skip the reseed
        // below whenever the new estimate lands within 5 ms of the stale one.
        _seededDelayMs = -1;

        FfiFrameObserver.AudioFrameReceived += OnFarEndFrame;
        _subscribed = true;

        SeedStreamDelay(0d);
    }

    public void Stop()
    {
        if (!_subscribed) return;

        FfiFrameObserver.AudioFrameReceived -= OnFarEndFrame;
        _subscribed = false;
        _clock.Reset();
    }

    /// <summary>
    /// Feeds near-end capture. Unity audio thread. Returns false when the block cannot be
    /// processed, and the caller must publish it unchanged — the APM needs a rate whose 10 ms
    /// chunk is a whole number of samples, and device audio backends do run at odd rates.
    /// </summary>
    public bool TryPushCapture(float[] data, int channels, int sampleRate)
    {
        if (_disposed || data == null || channels <= 0 || sampleRate <= 0) return false;

        if (!AudioProcessingModule.IsSupportedApiRate(sampleRate))
        {
            WarnUnsupportedRateOnce(sampleRate);
            return false;
        }

        _capturePump.Push(data, channels, sampleRate);

        var captureBlockMs = data.Length / (double)channels * 1000d / sampleRate;
        MaybeReseed(captureBlockMs);
        MaybeLogDiagnostics(channels, sampleRate);
        return true;
    }

    private void WarnUnsupportedRateOnce(int sampleRate)
    {
        if (Interlocked.Exchange(ref _unsupportedRateWarned, 1) == 1) return;

        Debug.LogWarning(
            $"[AEC] capture rate {sampleRate} has no whole-sample 10 ms chunk — " +
            "echo cancellation disabled, publishing microphone unprocessed");
    }

    // The far-end DataPtr is valid for the duration of this callback ONLY; the pump copies out
    // before doing anything else.
    private void OnFarEndFrame(RawAudioFrame frame)
    {
        if (_disposed) return;

        Interlocked.Increment(ref _farEndFrames);
        _renderPump.Push(frame.DataPtr, frame.SamplesPerChannel, frame.NumChannels, frame.SampleRate);
    }

    private void RaiseCaptureProcessed(float[] data, int channels, int sampleRate)
    {
        CaptureProcessed?.Invoke(data, channels, sampleRate);
    }

    // AVAudioSession reports zero latency until the session goes active, so the seed is
    // re-evaluated on a slow cadence rather than only once at Start().
    private void MaybeReseed(double captureBlockMs)
    {
        if (!_clock.IsRunning) return;

        var elapsedMs = _clock.ElapsedMilliseconds;
        if (elapsedMs < _nextSeedMs) return;
        _nextSeedMs = elapsedMs + SeedIntervalMs;

        SeedStreamDelay(captureBlockMs);
    }

    private void SeedStreamDelay(double captureBlockMs)
    {
        var delayMs = AudioProcessingDelaySeed.Estimate(captureBlockMs);
        if (_seededDelayMs >= 0 && Math.Abs(delayMs - _seededDelayMs) < SeedChangeThresholdMs) return;

        var error = _apm.SetStreamDelayMs(delayMs);
        if (error != null)
        {
            Debug.LogWarning($"[AEC] set_stream_delay_ms({delayMs}) failed: {error}");
            return;
        }

        _seededDelayMs = delayMs;
        Debug.Log($"[AEC] stream delay seeded to {delayMs}ms (captureBlock={captureBlockMs:F1}ms)");
    }

    // Reports the measured frame geometry both feeds are actually running at. Stopwatch, not
    // UnityEngine.Time: this is the audio thread.
    private void MaybeLogDiagnostics(int channels, int sampleRate)
    {
        if (!_clock.IsRunning) return;

        var elapsedMs = _clock.ElapsedMilliseconds;
        if (elapsedMs < _nextDiagnosticMs) return;
        _nextDiagnosticMs = elapsedMs + DiagnosticIntervalMs;

        Debug.Log(
            $"[AEC] capture {channels}ch@{sampleRate} native={AudioProcessingModule.IsNativeSampleRate(sampleRate)} " +
            $"chunks={_capturePump.ProcessedChunkCount} dropped={_capturePump.DroppedSamples} " +
            $"failed={_capturePump.FailedChunkCount} err={_capturePump.LastError ?? "-"} | " +
            $"render {_renderPump.Channels}ch@{_renderPump.SampleRate} frames={_farEndFrames} " +
            $"chunks={_renderPump.ProcessedChunkCount} dropped={_renderPump.DroppedSamples} " +
            $"failed={_renderPump.FailedChunkCount} err={_renderPump.LastError ?? "-"} | " +
            $"delay={_seededDelayMs}ms");
    }

    public void Dispose()
    {
        if (_disposed) return;

        Stop();
        _disposed = true;
        CaptureProcessed = null;
        _capturePump.Dispose();
        _renderPump.Dispose();
        _apm.Dispose();
    }
}
