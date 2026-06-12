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
        // --- Playback pacing servo ---
        // The mic clip is filled by the capture device's clock while the AudioSource that plays it
        // (feeding AudioProbe/OnAudioFilterRead) is driven by the output device's clock. With a
        // small, unmanaged startup lag the read head collides with the (bursty) write head and the
        // captured audio chops.
        //
        // Microphone.GetPosition cannot be trusted directly: on macOS with a Bluetooth HFP headset
        // its counter advances ~3.2x faster than data is actually written (clip labeled 16kHz,
        // counter at ~51k/s). The clip DATA is still genuinely at clip.frequency — playing it at
        // 1x yields correct-pitch voice, while reading at the counter's pace yields garbled
        // repeats. So the servo keeps pitch at ~1.0 and uses the counter only after rescaling by
        // its measured inflation factor k (counterRate / clip.frequency, ~1 on healthy devices) to
        // estimate the real write head, holding the read head a generous lag behind it with only
        // tiny pitch trims.
        private const float PreRollSeconds = 0.3f;        // counter-rate measurement window
        private const float DefaultTargetLagSec = 0.15f;  // initial read-behind-write lag
        private const float MinTargetLagSec = 0.10f;
        private const float MaxTargetLagSec = 0.40f;      // adaptive ceiling (jittery devices)
        private const float TrimGain = 0.5f;              // proportional gain on relative lag error
        private const float MaxPitchTrim = 0.03f;         // pitch stays within [0.97, 1.03]

        private readonly GameObject _sourceObject;
        private readonly string _deviceName;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;

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
            MonoBehaviourContext.RunCoroutine(StartMicrophone());

            _started = true;
        }

        // Opens the microphone at the engine's output sample rate when the device supports it, so
        // the captured clip and the AudioSource that plays it back run at the same rate. A mismatch
        // makes the looping clip drift against the playback read position and produces choppy audio.
        // Falls back to DefaultMicrophoneSampleRate when the output rate is unknown, and clamps to
        // the device's supported range when it reports one.
        private static int ResolveMicrophoneSampleRate(string deviceName)
        {
            int target = AudioSettings.outputSampleRate;
            if (target <= 0)
                target = (int)DefaultMicrophoneSampleRate;

            Microphone.GetDeviceCaps(deviceName, out int minFreq, out int maxFreq);
            // Unity reports (0, 0) when the device imposes no specific sample-rate range.
            if (minFreq == 0 && maxFreq == 0)
                return target;

            var result = Mathf.Clamp(target, minFreq, maxFreq);
            Utils.Info($"ResolveMicrophoneSampleRate: {result}");

            return result;
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

            AudioClip clip = null;
            var micFrequency = ResolveMicrophoneSampleRate(_deviceName);
            try
            {
                clip = Microphone.Start(
                    _deviceName,
                    loop: true,
                    lengthSec: 2,
                    frequency: micFrequency
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

            Utils.Info($"MicrophoneSource device='{_deviceName}' opened at {micFrequency}Hz (output={AudioSettings.outputSampleRate}Hz)");

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

            // Playback is started by the pacing servo, which first measures the clip's true fill
            // rate so the initial pitch and read position are right from the first sample.
            MonoBehaviourContext.RunCoroutine(PaceMicrophonePlayback(source, clip));
#if UNITY_EDITOR
            MonoBehaviourContext.RunCoroutine(DumpClipOnce(clip));
#endif
            Utils.Debug($"MicrophoneSource device='{_deviceName}' started successfully");
        }

#if UNITY_EDITOR
        // TEMP diagnostic: snapshots the raw mic clip to a WAV so its contents can be inspected
        // offline — is it one contiguous audio stream, or voice fragments scattered between stale
        // regions? Speak continuously for the first ~5 seconds of capture. Editor-only.
        private IEnumerator DumpClipOnce(AudioClip clip)
        {
            yield return new WaitForSeconds(4f);
            if (_disposed || clip == null) yield break;
            try
            {
                var data = new float[clip.samples * clip.channels];
                clip.GetData(data, 0);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lk_mic_clip.wav");
                WriteWav(path, data, clip.channels, clip.frequency);
                Utils.Info($"MicrophoneSource: dumped clip snapshot to {path} ({clip.samples} frames @ {clip.frequency}Hz/{clip.channels}ch)");
            }
            catch (Exception e)
            {
                Utils.Warning($"MicrophoneSource: clip dump failed: {e.Message}");
            }
        }

        private static void WriteWav(string path, float[] samples, int channels, int sampleRate)
        {
            using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create);
            using var w = new System.IO.BinaryWriter(fs);
            int dataBytes = samples.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);              // PCM
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);
            w.Write((short)(channels * 2)); // block align
            w.Write((short)16);             // bits per sample
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            foreach (var s in samples)
                w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
#endif

        // Keeps the AudioSource's read head a fixed lag behind the (estimated) real write head
        // (see the servo comment at the top of the class). Pitch stays ~1.0 — the clip data rate
        // IS clip.frequency — with only tiny trims; the added latency is the held lag itself.
        private IEnumerator PaceMicrophonePlayback(AudioSource source, AudioClip clip)
        {
            int clipFrames = clip.samples;
            int declaredRate = clip.frequency;

            // Pre-roll: measure how fast GetPosition's counter advances. Its instantaneous value
            // can be jumpy, but its average advance is steady (±0.1% measured), so a short window
            // gives a reliable rate. k is the counter's inflation relative to the data rate.
            int prevCounter = Microphone.GetPosition(_deviceName);
            long counterUnwrapped = prevCounter; // counter ran since Microphone.Start; small so far
            long preRollStart = counterUnwrapped;
            var preRoll = System.Diagnostics.Stopwatch.StartNew();
            while (preRoll.Elapsed.TotalSeconds < PreRollSeconds)
            {
                if (!_started || _disposed || source == null || !Microphone.IsRecording(_deviceName)) yield break;
                yield return null;
                int c = Microphone.GetPosition(_deviceName);
                counterUnwrapped += ((c - prevCounter) % clipFrames + clipFrames) % clipFrames;
                prevCounter = c;
            }
            if (!_started || _disposed || source == null) yield break;

            double counterRate = (counterUnwrapped - preRollStart) / preRoll.Elapsed.TotalSeconds;
            if (counterRate <= 0) counterRate = declaredRate;
            double k = counterRate / declaredRate; // ~1 on healthy devices, ~3.2 on macOS BT-HFP

            // Lag target, bounded by the clip's data capacity (clipFrames samples).
            float capacityCapSec = 0.4f * clipFrames / declaredRate;
            float targetLagSec = Mathf.Min(DefaultTargetLagSec, capacityCapSec);
            double target = targetLagSec * declaredRate;

            // Estimated real write head in data samples: the counter rescaled by k (both started
            // at zero when capture began).
            double writeEst = counterUnwrapped / k;

            source.pitch = 1f;
            source.Play();
            int startRead = (int)((((long)(writeEst - target)) % clipFrames + clipFrames) % clipFrames);
            source.timeSamples = startRead;
            int prevRead = startRead;
            double lag = target; // data samples the reader trails the estimated writer
            double smoothedLag = lag;
            double jitter = 0;

            Utils.Info($"MicrophoneSource pacing: counter={counterRate:F0}/s k={k:F2} dataRate={declaredRate}Hz lag={targetLagSec * 1000:F0}ms");

            long counterWindow = 0;
            var rateWindow = System.Diagnostics.Stopwatch.StartNew();
            var statusWindow = System.Diagnostics.Stopwatch.StartNew();

            while (_started && !_disposed && source != null && Microphone.IsRecording(_deviceName))
            {
                yield return null;
                if (source == null) yield break;

                int c = Microphone.GetPosition(_deviceName);
                int r = source.timeSamples;
                // Unwrapped per-frame advances. A hitch longer than the clip aliases these; the
                // resync guard below recovers from the resulting inconsistency.
                long dc = ((c - prevCounter) % clipFrames + clipFrames) % clipFrames;
                long dr = ((r - prevRead) % clipFrames + clipFrames) % clipFrames;
                prevCounter = c;
                prevRead = r;
                counterUnwrapped += dc;
                counterWindow += dc;
                lag += dc / k - dr;

                smoothedLag = 0.95 * smoothedLag + 0.05 * lag;
                jitter = 0.95 * jitter + 0.05 * Math.Abs(lag - smoothedLag);

                // Refine the counter rate and adapt the lag target once per second.
                if (rateWindow.Elapsed.TotalSeconds >= 1.0)
                {
                    double instRate = counterWindow / rateWindow.Elapsed.TotalSeconds;
                    if (instRate > 0)
                    {
                        counterRate = 0.7 * counterRate + 0.3 * instRate;
                        k = counterRate / declaredRate;
                    }
                    counterWindow = 0;
                    rateWindow.Restart();

                    // Hold ~4x the observed jitter as safety margin, within bounds and capacity.
                    float jitterSec = (float)(jitter / declaredRate);
                    targetLagSec = Mathf.Min(Mathf.Clamp(jitterSec * 4f, MinTargetLagSec, MaxTargetLagSec), capacityCapSec);
                    target = targetLagSec * declaredRate;
                }

                // Tiny proportional pitch trim toward the target lag. The data rate is
                // clip.frequency, so pitch must stay pinned near 1.
                double relErr = (smoothedLag - target) / target;
                relErr = Math.Max(-1.0, Math.Min(1.0, relErr));
                source.pitch = 1f + Mathf.Clamp((float)(TrimGain * relErr) * MaxPitchTrim, -MaxPitchTrim, MaxPitchTrim);

                // Out of bounds (reader overran the writer, or fell so far behind it reads
                // overwritten data): jump back to the target lag. Audible once, then stable.
                if (lag < 0 || lag > clipFrames * 0.9)
                {
                    int resyncRead = (int)((((long)(counterUnwrapped / k - target)) % clipFrames + clipFrames) % clipFrames);
                    source.timeSamples = resyncRead;
                    prevRead = resyncRead;
                    lag = target;
                    smoothedLag = target;
                    Utils.Warning($"MicrophoneSource pacing: resync, lag reset to {targetLagSec * 1000:F0}ms (k={k:F2} pitch={source.pitch:F3})");
                }

                if (statusWindow.Elapsed.TotalSeconds >= 5.0)
                {
                    Utils.Info($"MicrophoneSource pacing: k={k:F2} pitch={source.pitch:F3} lag={smoothedLag / declaredRate * 1000:F0}ms target={targetLagSec * 1000:F0}ms jitter={jitter / declaredRate * 1000:F1}ms");
                    statusWindow.Restart();
                }
            }
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
            if (Microphone.IsRecording(_deviceName))
                Microphone.End(_deviceName);

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

            Utils.Debug($"MicrophoneSource device='{_deviceName}' stopped");
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