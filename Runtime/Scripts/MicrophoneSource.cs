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
        // --- Capture design ---
        // The microphone clip's ring buffer is read directly (no AudioSource playback, no
        // OnAudioFilterRead), so capture is decoupled from the output device's clock.
        //
        // Microphone.GetPosition cannot be trusted as a sample position on every platform. On
        // macOS with a Bluetooth HFP headset, FMOD writes each real 20ms packet of clip.frequency
        // audio, then advances the position counter ~3.2x too far and zero-fills the skipped
        // range. The buffer then holds valid fragments of N samples at a stride J (measured: 320
        // of every 1024) and the counter rate is k = J/N times the data rate. Inspection of a raw
        // buffer dump showed the fragments are consecutive speech that joins continuously, so the
        // stream is reconstructed losslessly by reading only the first N = J/k samples of each
        // stride. Healthy devices have k ~ 1 and use a plain contiguous read.
        //
        // The clip's data rate is clip.frequency (verified: fragments play at correct pitch), so
        // captured samples are resampled from clip.frequency to the fixed native-source rate.
        private const uint TargetSampleRate = 48000;
        private const float PreRollSeconds = 0.3f;
        private const double FragmentedKThreshold = 1.05;
        private const float MaxBacklogSeconds = 0.2f; // drop backlog beyond this after a stall

        private readonly string _deviceName;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;
        private volatile bool _capturing = false;

        // Streaming linear-resampler state (input = clip.frequency, output = TargetSampleRate).
        private double _resamplePos;
        private float _resamplePrev;

        /// <summary>
        /// Creates a new microphone source for the given device.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">Unused; retained for compatibility. The microphone clip is read
        /// directly, so no scene GameObject/AudioSource is required.</param>
        public MicrophoneSource(string deviceName, GameObject sourceObject)
            : base(RtcAudioSourceType.AudioSourceMicrophone, TargetSampleRate, 1)
        {
            _deviceName = deviceName;
        }

        // The rate requested from Microphone.Start (a hint the platform may not honor), clamped to
        // the device's reported range. The authoritative data rate is clip.frequency afterwards.
        private static int ResolveRequestedSampleRate(string deviceName)
        {
            Microphone.GetDeviceCaps(deviceName, out int minFreq, out int maxFreq);
            if (minFreq == 0 && maxFreq == 0)
                return (int)TargetSampleRate;
            return Mathf.Clamp((int)TargetSampleRate, minFreq, maxFreq);
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
            int requestedRate = ResolveRequestedSampleRate(_deviceName);
            try
            {
                clip = Microphone.Start(
                    _deviceName,
                    loop: true,
                    lengthSec: 2,
                    frequency: requestedRate
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

            Utils.Info($"MicrophoneSource device='{_deviceName}' clip={clip.frequency}Hz/{clip.channels}ch samples={clip.samples} requested={requestedRate}Hz target={TargetSampleRate}Hz");

            _capturing = true;
            MonoBehaviourContext.RunCoroutine(CaptureLoop(clip));
        }

        // Reads new samples from the clip's ring buffer each frame and pushes them to the native
        // source via AudioRead. Runs on the main thread; the native source's queue absorbs the
        // per-frame pacing jitter.
        private IEnumerator CaptureLoop(AudioClip clip)
        {
            int clipFrames = clip.samples;
            int channels = clip.channels;
            int dataRate = clip.frequency > 0 ? clip.frequency : (int)DefaultMicrophoneSampleRate;

            // Pre-roll: measure how fast the position counter advances (its average is steady even
            // when individual values jump) and the size of its smallest discrete jump.
            int prevCounter = Microphone.GetPosition(_deviceName);
            long advance = 0;
            long minJump = long.MaxValue;
            var preRoll = System.Diagnostics.Stopwatch.StartNew();
            while (preRoll.Elapsed.TotalSeconds < PreRollSeconds)
            {
                if (!_capturing || _disposed) yield break;
                yield return null;
                int c = Microphone.GetPosition(_deviceName);
                long d = ((c - prevCounter) % clipFrames + clipFrames) % clipFrames;
                prevCounter = c;
                advance += d;
                if (d > 0 && d < minJump) minJump = d;
            }
            if (!_capturing || _disposed) yield break;

            double counterRate = advance > 0 ? advance / preRoll.Elapsed.TotalSeconds : dataRate;
            double k = counterRate / dataRate;

            // Fragmented mode: the counter advances in jumps of `stride`, but only the first
            // `validPerStride` samples of each stride contain data; the rest is zero padding.
            bool fragmented = k > FragmentedKThreshold && minJump != long.MaxValue && minJump > 1;
            int stride = fragmented ? (int)minJump : 0;
            int validPerStride = fragmented ? Math.Max(1, (int)Math.Round(stride / k)) : 0;

            if (fragmented)
                Utils.Info($"MicrophoneSource: fragmented clip detected (k={k:F2}); reading {validPerStride} of every {stride} samples at {dataRate}Hz");
            else
                Utils.Info($"MicrophoneSource: contiguous capture (k={k:F2}) at {dataRate}Hz");

            _resamplePos = 0.0;
            _resamplePrev = 0f;
            long maxBacklog = (long)(counterRate * MaxBacklogSeconds);
            int readPos = prevCounter; // counter values land on jump boundaries
            long pending = 0;

            while (_capturing && !_disposed)
            {
                yield return null;

                int c = Microphone.GetPosition(_deviceName);
                long d = ((c - prevCounter) % clipFrames + clipFrames) % clipFrames;
                prevCounter = c;
                pending += d;

                // After a long stall, drop the oldest backlog instead of pushing a burst that
                // would overrun the native source's queue.
                if (pending > maxBacklog)
                {
                    long drop = pending - maxBacklog;
                    if (fragmented) drop -= drop % stride; // preserve stride alignment
                    readPos = (int)((readPos + drop) % clipFrames);
                    pending -= drop;
                    Utils.Warning($"MicrophoneSource: dropped {drop} buffered samples after a stall");
                }

                if (fragmented)
                {
                    while (pending >= stride)
                    {
                        EmitClipRange(clip, channels, dataRate, readPos, validPerStride, clipFrames);
                        readPos = (readPos + stride) % clipFrames;
                        pending -= stride;
                    }
                }
                else if (pending > 0)
                {
                    EmitClipRange(clip, channels, dataRate, readPos, (int)pending, clipFrames);
                    readPos = (int)((readPos + pending) % clipFrames);
                    pending = 0;
                }
            }
        }

        // Reads `count` frames starting at `start`, splitting at the ring wrap so each GetData
        // read is contiguous.
        private void EmitClipRange(AudioClip clip, int channels, int dataRate, int start, int count, int clipFrames)
        {
            if (count <= 0) return;
            int first = Math.Min(count, clipFrames - start);
            ReadAndPush(clip, channels, dataRate, start, first);
            if (count > first)
                ReadAndPush(clip, channels, dataRate, 0, count - first);
        }

        // Reads a contiguous range, downmixes to mono, resamples dataRate -> TargetSampleRate
        // (streaming linear interpolation carrying state across calls, so fragment junctions stay
        // continuous), and fires AudioRead.
        private void ReadAndPush(AudioClip clip, int channels, int dataRate, int start, int count)
        {
            if (count <= 0) return;

            var interleaved = new float[count * channels];
            clip.GetData(interleaved, start);

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
                    for (int ch = 0; ch < channels; ch++)
                        sum += interleaved[f * channels + ch];
                    mono[f] = sum / channels;
                }
            }

            double step = (double)dataRate / TargetSampleRate;
            var output = new List<float>((int)(count / step) + 2);

            // Index -1 maps to the carried last sample of the previous chunk so interpolation is
            // continuous across chunk boundaries. pos stays >= -1.
            double pos = _resamplePos;
            while (pos < count - 1)
            {
                int i0 = (int)Math.Floor(pos);
                float a = i0 < 0 ? _resamplePrev : mono[i0];
                float b = mono[i0 + 1];
                float frac = (float)(pos - i0);
                output.Add(a * (1f - frac) + b * frac);
                pos += step;
            }
            _resamplePrev = mono[count - 1];
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
