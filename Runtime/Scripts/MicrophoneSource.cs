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
        // Microphone.GetPosition cannot be trusted as a sample position on every platform; see
        // MicClipReader for the fragmented-buffer model (macOS + Bluetooth HFP) and how the
        // contiguous stream is reconstructed from it.
        //
        // The clip's data rate is clip.frequency (verified: fragments play at correct pitch), so
        // captured samples are resampled from clip.frequency to the fixed native-source rate.
        private const uint TargetSampleRate = 48000;
        private const float PreRollSeconds = 0.3f;
        private const float SettleSeconds = 0.1f;     // discard the counter's startup burst before measuring
        // Engaging fragmented mode discards (stride - valid) samples per stride, so a false
        // positive guarantees audio loss while a false negative only risks mild artifacts. The
        // observed pathological device measures k=3.2; healthy devices measure ~1.0 with up to a
        // few percent of startup noise. Keep a wide margin between the two.
        private const double FragmentedKThreshold = 1.5;
        private const float MaxBacklogSeconds = 0.2f; // drop backlog beyond this after a stall

        private readonly string _deviceName;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;
        private volatile bool _capturing = false;

        private StreamingResampler _resampler;

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
        // source via AudioRead. MicClipReader decides what to read (including reconstructing
        // fragmented buffers); this loop is the thin Unity shell around it. Runs on the main
        // thread; the native source's queue absorbs the per-frame pacing jitter.
        private IEnumerator CaptureLoop(AudioClip clip)
        {
            int clipFrames = clip.samples;
            int channels = clip.channels;
            int dataRate = clip.frequency > 0 ? clip.frequency : (int)DefaultMicrophoneSampleRate;

            var reader = new MicClipReader(clipFrames, dataRate, PreRollSeconds, FragmentedKThreshold, MaxBacklogSeconds, SettleSeconds);
            _resampler = new StreamingResampler(dataRate, (int)TargetSampleRate);
            var ranges = new List<MicClipReader.ReadRange>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool announced = false;
            long reportedDrops = 0;

            while (_capturing && !_disposed)
            {
                yield return null;

                ranges.Clear();
                reader.Update(Microphone.GetPosition(_deviceName), clock.Elapsed.TotalSeconds, ranges);

                if (!announced && reader.Ready)
                {
                    announced = true;
                    if (reader.Fragmented)
                        Utils.Info($"MicrophoneSource: fragmented clip detected (k={reader.K:F2}); reading {reader.ValidPerStride} of every {reader.Stride} samples at {dataRate}Hz");
                    else
                        Utils.Info($"MicrophoneSource: contiguous capture (k={reader.K:F2}) at {dataRate}Hz");
                }

                if (reader.TotalDropped > reportedDrops)
                {
                    Utils.Warning($"MicrophoneSource: dropped {reader.TotalDropped - reportedDrops} buffered samples after a stall");
                    reportedDrops = reader.TotalDropped;
                }

                for (int i = 0; i < ranges.Count; i++)
                    ReadAndPush(clip, channels, ranges[i].Start, ranges[i].Count);
            }
        }

        // Reads a contiguous range, downmixes to mono, resamples clip.frequency ->
        // TargetSampleRate (the resampler carries state across calls, so fragment junctions stay
        // continuous), and fires AudioRead.
        private void ReadAndPush(AudioClip clip, int channels, int start, int count)
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

            var output = _resampler.Process(mono, count);
            if (output.Length > 0)
                AudioRead?.Invoke(output, 1, (int)TargetSampleRate);
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
