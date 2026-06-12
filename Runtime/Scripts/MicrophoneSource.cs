using System;
using System.Collections;
using System.Collections.Generic;
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
        // Unity sometimes misreports the microphone clip's sample rate (e.g. a Bluetooth headset in
        // a bad audio-routing state reports clip.frequency=16000 while the clip is actually filled
        // at ~48-51 kHz). So we push to the native source at a fixed, trusted rate and resample the
        // captured audio — whose true rate we measure at runtime from GetPosition — to it.
        private const uint TargetSampleRate = 48000;
        private const float RateMeasureSeconds = 0.3f;

        private readonly string _deviceName;
        private readonly uint _requestedRate;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;
        private volatile bool _capturing = false;
        private int _lastReadPos = 0;

        // Streaming linear-resampler state (input = measured mic rate, output = TargetSampleRate).
        private double _resamplePos = 0.0;
        private float _resamplePrev = 0f;

        /// <summary>
        /// Creates a new microphone source for the given device.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">Unused; retained for backwards compatibility. The microphone is now read
        /// directly from its clip, so no scene GameObject/AudioSource is required.</param>
        public MicrophoneSource(string deviceName, GameObject sourceObject)
            : base(RtcAudioSourceType.AudioSourceMicrophone, TargetSampleRate, 1)
        {
            _deviceName = deviceName;
            _requestedRate = ResolveMicrophoneSampleRate(deviceName);
        }

        // The rate we ask Microphone.Start for (a hint Unity may ignore). Clamped into the device's
        // reported range; the actual captured rate is measured at runtime and may differ.
        private static uint ResolveMicrophoneSampleRate(string deviceName)
        {
            Microphone.GetDeviceCaps(deviceName, out int minFreq, out int maxFreq);
            if (minFreq == 0 && maxFreq == 0)
                return TargetSampleRate;
            return (uint)Mathf.Clamp((int)TargetSampleRate, minFreq, maxFreq);
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
                    frequency: (int)_requestedRate
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

            Utils.Info($"MicrophoneSource device='{_deviceName}' clip={clip.frequency}Hz/{clip.channels}ch samples={clip.samples} requested={_requestedRate}Hz target={TargetSampleRate}Hz");

            _capturing = true;
            MonoBehaviourContext.RunCoroutine(CaptureLoop(clip));
        }

        // Reads new microphone samples straight from the looping clip's ring buffer, resamples them
        // from the device's true (measured) rate to TargetSampleRate, and pushes them. Reading the
        // ring buffer directly (instead of playing the clip and tapping OnAudioFilterRead) avoids the
        // playback-vs-capture clock drift that produced choppy audio. Runs on the main thread; the
        // native source's queue absorbs the per-frame pacing jitter.
        private IEnumerator CaptureLoop(AudioClip clip)
        {
            int clipFrames = clip.samples;   // frames per channel in the loop buffer
            int channels = clip.channels;

            // clip.frequency is unreliable in some device states, so measure the true capture rate
            // from how fast GetPosition advances over a short window before we start pushing.
            int prev = Microphone.GetPosition(_deviceName);
            long advance = 0;
            var measureSw = System.Diagnostics.Stopwatch.StartNew();
            while (measureSw.Elapsed.TotalSeconds < RateMeasureSeconds && _capturing && !_disposed)
            {
                yield return null;
                int p = Microphone.GetPosition(_deviceName);
                advance += ((p - prev) % clipFrames + clipFrames) % clipFrames;
                prev = p;
            }
            if (!_capturing || _disposed) yield break;

            double measuredSecs = measureSw.Elapsed.TotalSeconds;
            double realRate = (measuredSecs > 0 && advance > 0) ? ClampRate(advance / measuredSecs) : clip.frequency;
            Utils.Info($"MicrophoneSource: measured capture rate {realRate:F0}Hz (clip.frequency={clip.frequency}Hz), resampling to {TargetSampleRate}Hz");

            ResetResampler();
            _lastReadPos = Microphone.GetPosition(_deviceName);

            // Refine the rate estimate as we go so slow clock drift can't make the native buffer
            // creep toward over/underrun.
            long readSinceRefine = 0;
            var refineSw = System.Diagnostics.Stopwatch.StartNew();

            while (_capturing && !_disposed)
            {
                int micPos = Microphone.GetPosition(_deviceName);
                // Drain everything between the last read and the write head, splitting at the ring
                // wrap so each GetData read is contiguous.
                while (_lastReadPos != micPos)
                {
                    int end = micPos > _lastReadPos ? micPos : clipFrames;
                    int count = end - _lastReadPos;
                    EmitResampled(clip, channels, _lastReadPos, count, realRate);
                    readSinceRefine += count;
                    _lastReadPos = end % clipFrames;
                }

                double secs = refineSw.Elapsed.TotalSeconds;
                if (secs >= 1.0 && readSinceRefine > 0)
                {
                    realRate = 0.5 * realRate + 0.5 * ClampRate(readSinceRefine / secs); // EMA
                    readSinceRefine = 0;
                    refineSw.Restart();
                }

                yield return null;
            }
        }

        private static double ClampRate(double r) => r < 8000 ? 8000 : (r > 192000 ? 192000 : r);

        private void ResetResampler()
        {
            _resamplePos = 0.0;
            _resamplePrev = 0f;
        }

        // Reads `count` contiguous frames at `startFrame`, downmixes to mono, resamples from
        // `inputRate` to TargetSampleRate (streaming linear interpolation that carries state across
        // calls), and pushes the result. `startFrame + count` never exceeds the clip length (callers
        // split at the wrap), so the GetData read is always contiguous.
        private void EmitResampled(AudioClip clip, int channels, int startFrame, int count, double inputRate)
        {
            if (count <= 0) return;

            var interleaved = new float[count * channels];
            clip.GetData(interleaved, startFrame);

            float[] inMono;
            if (channels == 1)
            {
                inMono = interleaved;
            }
            else
            {
                inMono = new float[count];
                for (int f = 0; f < count; f++)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                        sum += interleaved[f * channels + c];
                    inMono[f] = sum / channels;
                }
            }

            double step = inputRate / TargetSampleRate; // input samples advanced per output sample
            var output = new List<float>((int)(count / step) + 2);

            // Index -1 maps to the carried last sample of the previous chunk so interpolation is
            // continuous across chunk/tick boundaries. pos stays >= -1.
            double pos = _resamplePos;
            while (pos < count - 1)
            {
                int i0 = (int)Math.Floor(pos);
                float a = i0 < 0 ? _resamplePrev : inMono[i0];
                float b = inMono[i0 + 1];
                float frac = (float)(pos - i0);
                output.Add(a * (1f - frac) + b * frac);
                pos += step;
            }
            _resamplePrev = inMono[count - 1];
            _resamplePos = pos - count;

            if (output.Count > 0)
                AudioRead?.Invoke(output.ToArray(), 1, (int)TargetSampleRate);
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