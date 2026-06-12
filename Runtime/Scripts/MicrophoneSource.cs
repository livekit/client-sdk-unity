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
        // (feeding AudioProbe/OnAudioFilterRead) is driven by the output device's clock. Worse,
        // some devices misreport the clip rate entirely (a Bluetooth headset on macOS labeled its
        // clip 16kHz while filling it at ~51kHz). Either way the read head drifts against the
        // write head until it gets lapped, which sounds like periodic chopping. The servo measures
        // how fast the write head actually advances and continuously adjusts AudioSource.pitch so
        // the read head consumes clip samples at the same rate, holding a fixed lag behind the
        // writer. In the normal case the measured rate matches clip.frequency and pitch stays ~1.
        private const float PreRollSeconds = 0.3f;       // initial fill-rate measurement window
        private const float MinTargetLagSec = 0.08f;     // smallest safety lag (good devices)
        private const float MaxTargetLagSec = 0.25f;     // adaptive ceiling (jittery devices)
        private const float PitchCorrectionGain = 0.5f;  // proportional gain on relative lag error
        private const float MaxRelativeCorrection = 0.2f;
        private const float MinPitch = 0.25f;
        private const float MaxPitch = 8f;

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
                    lengthSec: 1,
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
            Utils.Debug($"MicrophoneSource device='{_deviceName}' started successfully");
        }

        // Keeps the AudioSource's read head locked a fixed lag behind the mic's write head by
        // adjusting pitch (see the servo comment at the top of the class). Pitch is rate control,
        // not a delay: the only latency this adds is the target lag itself.
        private IEnumerator PaceMicrophonePlayback(AudioSource source, AudioClip clip)
        {
            int clipFrames = clip.samples;
            int declaredRate = clip.frequency;

            // Pre-roll: measure the true fill rate before playback starts. GetPosition's
            // instantaneous position can be jumpy on misbehaving devices, but its average advance
            // has measured steady (±0.1%), so a short window gives a reliable rate.
            int prevWrite = Microphone.GetPosition(_deviceName);
            long writeAdvance = 0;
            var preRoll = System.Diagnostics.Stopwatch.StartNew();
            while (preRoll.Elapsed.TotalSeconds < PreRollSeconds)
            {
                if (!_started || _disposed || source == null || !Microphone.IsRecording(_deviceName)) yield break;
                yield return null;
                int w = Microphone.GetPosition(_deviceName);
                writeAdvance += ((w - prevWrite) % clipFrames + clipFrames) % clipFrames;
                prevWrite = w;
            }
            if (!_started || _disposed || source == null) yield break;

            double fillRate = writeAdvance > 0 ? writeAdvance / preRoll.Elapsed.TotalSeconds : declaredRate;
            double basePitch = fillRate / declaredRate;
            // The lag target must stay well below the clip's real capacity (clipFrames at the
            // true fill rate), which can be much shorter than lengthSec when the rate is misreported.
            float capacityCapSec = (float)(0.4 * clipFrames / fillRate);
            float targetLagSec = Mathf.Min(MinTargetLagSec, capacityCapSec);

            source.pitch = Mathf.Clamp((float)basePitch, MinPitch, MaxPitch);
            source.Play();
            long targetLag = (long)(targetLagSec * fillRate);
            int startRead = (int)(((prevWrite - targetLag) % clipFrames + clipFrames) % clipFrames);
            source.timeSamples = startRead;

            Utils.Info($"MicrophoneSource pacing: measured={fillRate:F0}Hz declared={declaredRate}Hz pitch={source.pitch:F2} lag={targetLagSec * 1000:F0}ms");

            int prevRead = startRead;
            double lag = targetLag;          // current read-behind-write distance, in clip samples
            double smoothedLag = lag;
            double jitter = 0;
            long rateAdvance = 0;
            var rateWindow = System.Diagnostics.Stopwatch.StartNew();
            var statusWindow = System.Diagnostics.Stopwatch.StartNew();

            while (_started && !_disposed && source != null && Microphone.IsRecording(_deviceName))
            {
                yield return null;
                if (source == null) yield break;

                int w = Microphone.GetPosition(_deviceName);
                int r = source.timeSamples;
                // Unwrapped per-frame advances. A hitch longer than the clip aliases these; the
                // resync guard below recovers from the resulting inconsistency.
                long dw = ((w - prevWrite) % clipFrames + clipFrames) % clipFrames;
                long dr = ((r - prevRead) % clipFrames + clipFrames) % clipFrames;
                prevWrite = w;
                prevRead = r;
                lag += dw - dr;
                rateAdvance += dw;

                smoothedLag = 0.95 * smoothedLag + 0.05 * lag;
                jitter = 0.95 * jitter + 0.05 * Math.Abs(lag - smoothedLag);

                // Refine the fill rate and adapt the lag target once per second.
                if (rateWindow.Elapsed.TotalSeconds >= 1.0)
                {
                    double instRate = rateAdvance / rateWindow.Elapsed.TotalSeconds;
                    if (instRate > 0)
                    {
                        fillRate = 0.7 * fillRate + 0.3 * instRate;
                        basePitch = fillRate / declaredRate;
                    }
                    rateAdvance = 0;
                    rateWindow.Restart();

                    // Hold ~4x the observed jitter as safety margin, within bounds and capacity.
                    float jitterSec = (float)(jitter / fillRate);
                    capacityCapSec = (float)(0.4 * clipFrames / fillRate);
                    targetLagSec = Mathf.Min(Mathf.Clamp(jitterSec * 4f, MinTargetLagSec, MaxTargetLagSec), capacityCapSec);
                }

                // Proportional pitch correction toward the target lag.
                double target = targetLagSec * fillRate;
                double relErr = (smoothedLag - target) / target;
                relErr = Math.Max(-MaxRelativeCorrection, Math.Min(MaxRelativeCorrection, relErr));
                source.pitch = Mathf.Clamp((float)(basePitch * (1.0 + PitchCorrectionGain * relErr)), MinPitch, MaxPitch);

                // Out of bounds (reader overran the writer, or fell so far behind it reads
                // overwritten data): jump back to the target lag. Audible once, then stable.
                if (lag < 0 || lag > clipFrames * 0.9)
                {
                    int resyncRead = (int)(((w - (long)target) % clipFrames + clipFrames) % clipFrames);
                    source.timeSamples = resyncRead;
                    prevRead = resyncRead;
                    lag = target;
                    smoothedLag = target;
                    Utils.Warning($"MicrophoneSource pacing: resync, lag reset to {targetLagSec * 1000:F0}ms (rate={fillRate:F0}Hz pitch={source.pitch:F2})");
                }

                if (statusWindow.Elapsed.TotalSeconds >= 5.0)
                {
                    Utils.Info($"MicrophoneSource pacing: rate={fillRate:F0}Hz pitch={source.pitch:F2} lag={smoothedLag / fillRate * 1000:F0}ms target={targetLagSec * 1000:F0}ms jitter={jitter / fillRate * 1000:F1}ms");
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