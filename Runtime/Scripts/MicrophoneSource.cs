using System;
using System.Collections;
using UnityEngine;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    /// An audio source which captures from the device's microphone.
    /// </summary>
    /// <remarks>
    /// Ensure microphone permissions are granted before calling <see cref="Start"/>.
    /// </remarks>
    sealed public class MicrophoneSource : RtcAudioSource
    {
        private readonly GameObject _sourceObject;

        // The device requested by the caller. Empty/null means "follow the OS default".
        private readonly string _deviceName;

        // The device the microphone is actually recording from right now. This can differ from
        // _deviceName when the preferred device is unavailable and we fall back to the OS default,
        // so all Microphone.* calls (IsRecording/GetPosition/End) must use this name.
        private string _activeDeviceName;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;
        private bool _restarting = false;

        /// <summary>
        /// Creates a new microphone source for the given device.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">The GameObject to attach the AudioSource to. The object must be kept in the scene
        /// for the duration of the source's lifetime.</param>
        public MicrophoneSource(string deviceName, GameObject sourceObject) : base(RtcAudioSourceType.AudioSourceMicrophone)
        {
            _deviceName = deviceName;
            _sourceObject = sourceObject;
        }

        /// <summary>
        /// Begins capturing audio from the microphone.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the microphone is not available or unauthorized.
        /// </exception>
        /// <remarks>
        /// Ensure microphone permissions are granted before calling this method
        /// by calling <see cref="Application.RequestUserAuthorization"/>.
        /// </remarks>
        public override void Start()
        {
            base.Start();
            if (_started) return;


            if (!Application.HasUserAuthorization(mode: UserAuthorization.Microphone))
                throw new InvalidOperationException("Microphone access not authorized");

            MonoBehaviourContext.OnApplicationPauseEvent += OnApplicationPause;
            // Restart capture when the system audio device changes (e.g. a Bluetooth headset is
            // unplugged). Unity rebuilds its audio graph on a device change, which both detaches
            // the AudioProbe tap and leaves Microphone.Start bound to a now-gone device.
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            MonoBehaviourContext.RunCoroutine(StartMicrophone());

            _started = true;
        }

        private IEnumerator StartMicrophone()
        {
            // Validate that the GameObject is still valid before starting
            if (_sourceObject == null)
            {
                Utils.Error("MicrophoneSource: GameObject is null, cannot start microphone");
                yield break;
            }

            // Verify microphone is still authorized (could change during background)
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Utils.Error("MicrophoneSource: Microphone authorization lost");
                yield break;
            }

            // Resolve which device to record from. Falls back to the OS default when the
            // preferred device is gone, so an unplugged headset transparently hands off to the
            // built-in microphone.
            _activeDeviceName = ResolveCaptureDevice();

            AudioClip clip = null;
            try
            {
                clip = Microphone.Start(
                    _activeDeviceName,
                    loop: true,
                    lengthSec: 1,
                    frequency: (int)_expectedSampleRate
                );
            }
            catch (Exception e)
            {
                Utils.Error($"MicrophoneSource: Exception starting microphone: {e.Message}");
                yield break;
            }

            if (clip == null)
            {
                Utils.Error("MicrophoneSource: Microphone.Start returned null, audio session may not be ready");
                yield break;
            }

            // Ensure no duplicate components exist before adding new ones.
            // This is important during app resume on iOS where components might not be
            // fully destroyed yet due to Unity's deferred Destroy().
            var existingSource = _sourceObject.GetComponent<AudioSource>();
            if (existingSource != null)
                UnityEngine.Object.DestroyImmediate(existingSource);

            var existingProbe = _sourceObject.GetComponent<AudioProbe>();
            if (existingProbe != null)
            {
                existingProbe.AudioRead -= OnAudioRead;
                UnityEngine.Object.DestroyImmediate(existingProbe);
            }

            var source = _sourceObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;

            var probe = _sourceObject.AddComponent<AudioProbe>();
            // Clear the audio data after it is read as to not play it through the speaker locally.
            probe.ClearAfterInvocation();
            probe.AudioRead += OnAudioRead;

            // Wait for microphone to actually start producing data with a timeout
            const float timeout = 2f;
            float elapsed = 0f;
            while (Microphone.GetPosition(_activeDeviceName) <= 0 && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.05f);
                elapsed += 0.05f;
            }

            if (Microphone.GetPosition(_activeDeviceName) <= 0)
            {
                Utils.Error($"MicrophoneSource: Microphone did not start producing data after {timeout}s");
                yield break;
            }

            source.Play();
            Utils.Debug($"MicrophoneSource device='{_activeDeviceName ?? "<default>"}' started successfully");
        }

        /// <summary>
        /// Stops capturing audio from the microphone.
        /// </summary>
        public override void Stop()
        {
            base.Stop();
            MonoBehaviourContext.RunCoroutine(StopMicrophone());
            MonoBehaviourContext.OnApplicationPauseEvent -= OnApplicationPause;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            _started = false;
        }

        private IEnumerator StopMicrophone()
        {
            if (Microphone.IsRecording(_activeDeviceName))
                Microphone.End(_activeDeviceName);

            // Check if GameObject is still valid before trying to access components
            if (_sourceObject != null)
            {
                var probe = _sourceObject.GetComponent<AudioProbe>();
                if (probe != null)
                {
                    probe.AudioRead -= OnAudioRead;
                    UnityEngine.Object.Destroy(probe);
                }

                var source = _sourceObject.GetComponent<AudioSource>();
                if (source != null)
                    UnityEngine.Object.Destroy(source);
            }

            Utils.Debug($"MicrophoneSource device='{_activeDeviceName ?? "<default>"}' stopped");
            yield return null;
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            AudioRead?.Invoke(data, channels, sampleRate);
        }

        private void OnApplicationPause(bool pause)
        {
            if (!_started)
                return;

            if (pause)
            {
                // On iOS, when app goes to background, we should stop using audio resources
                // to avoid AVAudioSession interruption errors (FigCaptureSourceRemote -17281)
                MonoBehaviourContext.RunCoroutine(StopMicrophone());
            }
            else
            {
                // When resuming, restart the microphone
                MonoBehaviourContext.RunCoroutine(RestartMicrophone());
            }
        }

        // Picks the device name to pass to Microphone.Start. An empty preferred name, or a
        // preferred device that is no longer connected, resolves to null so Unity records from
        // the current OS default device.
        private string ResolveCaptureDevice()
        {
            if (string.IsNullOrEmpty(_deviceName))
                return null;

            if (Array.IndexOf(Microphone.devices, _deviceName) >= 0)
                return _deviceName;

            Utils.Debug($"MicrophoneSource: preferred device '{_deviceName}' is no longer available, falling back to the OS default");
            return null;
        }

        // Fires on the main thread when Unity's audio configuration changes, including when the
        // system capture/playback device changes (e.g. unplugging a Bluetooth headset). Mirrors
        // AudioStream.OnAudioConfigurationChanged on the playback side.
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (!_started)
                return;

            // The native source's rate is fixed at construction and RtcAudioSource drops frames
            // whose rate doesn't match it. If the device change moved Unity's DSP output rate,
            // restarting capture alone won't recover audio — warn so the silence is diagnosable.
            // Full recovery (recreating the native source at the new rate) is handled separately.
            var outputSampleRate = (uint)AudioSettings.outputSampleRate;
            if (outputSampleRate != _expectedSampleRate)
            {
                Utils.Warning($"MicrophoneSource: audio device change moved the DSP output rate to {outputSampleRate}Hz, but the native source is fixed at {_expectedSampleRate}Hz. Captured frames will be dropped until the track is recreated at the new rate.");
            }

            if (deviceWasChanged)
            {
                Utils.Debug("MicrophoneSource: audio device changed, restarting capture on the current default device");
                MonoBehaviourContext.RunCoroutine(RestartMicrophone());
            }
        }

        private IEnumerator RestartMicrophone()
        {
            // The device-change event can fire several times around a single hardware swap;
            // ignore re-entrant restarts so overlapping Stop/Start coroutines don't race.
            if (_restarting)
                yield break;
            _restarting = true;

            yield return StopMicrophone();

            // Wait for iOS audio session to be ready before attempting to restart.
            // On iOS, after app resumes from background, the audio session needs time to
            // recover from interruption. Poll for readiness instead of using arbitrary delay.
            yield return WaitForMicrophoneReady();

            yield return StartMicrophone();

            _restarting = false;
        }

        private IEnumerator WaitForMicrophoneReady()
        {
            // Wait for microphone devices to become available again after iOS audio session interruption.
            // This is more reliable than a fixed delay because we wait for actual system readiness.
            const float timeout = 2f;
            float elapsed = 0f;

            // On iOS, Microphone.devices may be empty immediately after resume while
            // AVAudioSession is recovering from interruption. Wait until devices are available.
            while (Microphone.devices.Length == 0 && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.05f);
                elapsed += 0.05f;
            }

            if (Microphone.devices.Length == 0)
            {
                Utils.Error($"MicrophoneSource: Microphone devices not available after {timeout}s timeout");
                yield break;
            }

            // Extra frame to ensure audio session is fully ready
            yield return null;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) Stop();
            _disposed = true;
            base.Dispose(disposing);
        }

        ~MicrophoneSource()
        {
            Dispose(false);
        }
    }
}