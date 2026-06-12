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
        private readonly string _deviceName;
        private readonly uint _micSampleRate;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;
        private volatile bool _capturing = false;
        private int _lastReadPos = 0;

        /// <summary>
        /// Creates a new microphone source for the given device.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">Unused; retained for backwards compatibility. The microphone is now read
        /// directly from its clip, so no scene GameObject/AudioSource is required.</param>
        public MicrophoneSource(string deviceName, GameObject sourceObject)
            : base(RtcAudioSourceType.AudioSourceMicrophone, ResolveMicrophoneSampleRate(deviceName), 1)
        {
            _deviceName = deviceName;
            _micSampleRate = ResolveMicrophoneSampleRate(deviceName);
        }

        // Picks the capture rate before the microphone is started so the native source can be
        // created with a matching format. Clamps the preferred rate into the device's supported
        // range (Unity reports (0, 0) when the device imposes no specific range).
        private static uint ResolveMicrophoneSampleRate(string deviceName)
        {
            Microphone.GetDeviceCaps(deviceName, out int minFreq, out int maxFreq);
            if (minFreq == 0 && maxFreq == 0)
                return DefaultMicrophoneSampleRate;
            return (uint)Mathf.Clamp((int)DefaultMicrophoneSampleRate, minFreq, maxFreq);
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
            MonoBehaviourContext.RunCoroutine(StartMicrophone());

            _started = true;
        }

        private IEnumerator StartMicrophone()
        {
            // Verify microphone is still authorized (could change during background)
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Utils.Error("MicrophoneSource: Microphone authorization lost");
                yield break;
            }

            AudioClip clip = null;
            try
            {
                clip = Microphone.Start(
                    _deviceName,
                    loop: true,
                    lengthSec: 1,
                    frequency: (int)_micSampleRate
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

            // Wait for microphone to actually start producing data with a timeout
            const float timeout = 2f;
            float elapsed = 0f;
            while (Microphone.GetPosition(_deviceName) <= 0 && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.05f);
                elapsed += 0.05f;
            }

            if (Microphone.GetPosition(_deviceName) <= 0)
            {
                Utils.Error($"MicrophoneSource: Microphone did not start producing data after {timeout}s");
                yield break;
            }

            Utils.Info($"MicrophoneSource device='{_deviceName}' capturing clip={clip.frequency}Hz/{clip.channels}ch nativeRate={_micSampleRate}Hz");

            _capturing = true;
            MonoBehaviourContext.RunCoroutine(CaptureLoop(clip));
        }

        // Reads new microphone samples straight from the looping clip's ring buffer each frame and
        // pushes them to the native source. Unlike playing the clip through an AudioSource and
        // tapping OnAudioFilterRead, this has no playback read cursor to drift against the mic
        // write cursor, so it avoids the periodic gaps that produced choppy audio. Runs on the main
        // thread; the native source's queue absorbs the per-frame pacing jitter.
        private IEnumerator CaptureLoop(AudioClip clip)
        {
            int clipFrames = clip.samples;   // frames per channel in the loop buffer
            int channels = clip.channels;
            int rate = clip.frequency;

            // The native source was created for _micSampleRate. If Unity opened the device at a
            // different rate (e.g. the device changed since construction), drop rather than push a
            // mismatch the native side would reject; recovery is a track restart.
            if ((uint)rate != _micSampleRate)
            {
                Utils.Warning($"MicrophoneSource: clip rate {rate}Hz does not match native source rate {_micSampleRate}Hz; not capturing (restart the track to recover)");
                yield break;
            }

            _lastReadPos = Microphone.GetPosition(_deviceName);

            while (_capturing && !_disposed)
            {
                int micPos = Microphone.GetPosition(_deviceName);
                // Drain everything between the last read and the write head, splitting at the ring
                // wrap so each GetData read is contiguous.
                while (_lastReadPos != micPos)
                {
                    int end = micPos > _lastReadPos ? micPos : clipFrames;
                    int count = end - _lastReadPos;
                    EmitSamples(clip, channels, rate, _lastReadPos, count);
                    _lastReadPos = end % clipFrames;
                }
                yield return null;
            }
        }

        // Reads `count` contiguous frames starting at `startFrame`, downmixes to mono if needed,
        // and fires AudioRead. `startFrame + count` never exceeds the clip length (callers split at
        // the wrap), so a single GetData is always contiguous.
        private void EmitSamples(AudioClip clip, int channels, int rate, int startFrame, int count)
        {
            if (count <= 0) return;

            var interleaved = new float[count * channels];
            clip.GetData(interleaved, startFrame);

            float[] mono;
            if (channels == 1)
            {
                mono = interleaved;
            }
            else
            {
                mono = new float[count];
                for (int f = 0; f < count; f++)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                        sum += interleaved[f * channels + c];
                    mono[f] = sum / channels;
                }
            }

            AudioRead?.Invoke(mono, 1, rate);
        }

        /// <summary>
        /// Stops capturing audio from the microphone.
        /// </summary>
        public override void Stop()
        {
            base.Stop();
            MonoBehaviourContext.RunCoroutine(StopMicrophone());
            MonoBehaviourContext.OnApplicationPauseEvent -= OnApplicationPause;
            _started = false;
        }

        private IEnumerator StopMicrophone()
        {
            _capturing = false;

            if (Microphone.IsRecording(_deviceName))
                Microphone.End(_deviceName);

            Utils.Debug($"MicrophoneSource device='{_deviceName}' stopped");
            yield return null;
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

        private IEnumerator RestartMicrophone()
        {
            yield return StopMicrophone();

            // Wait for iOS audio session to be ready before attempting to restart.
            // On iOS, after app resumes from background, the audio session needs time to
            // recover from interruption. Poll for readiness instead of using arbitrary delay.
            yield return WaitForMicrophoneReady();

            yield return StartMicrophone();
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