using System;
using System.Collections;
using UnityEngine;
using LiveKit.Internal;

using LiveKit.Internal.Threading;
namespace LiveKit
{
    /// <summary>
    /// An audio source which captures from the device's microphone.
    /// </summary>
    /// <remarks>
    /// Ensure microphone permissions are granted before calling <see cref="Start"/>.
    /// </remarks>
    /// <summary>
    /// Strategy used by <see cref="MicrophoneSource"/> to move samples from Unity's microphone
    /// ring buffer into the audio pipeline.
    /// </summary>
    public enum MicrophoneCaptureMode
    {
        /// <summary>
        /// Play the microphone clip through an <see cref="AudioSource"/> and capture it via
        /// <see cref="AudioProbe"/> (OnAudioFilterRead). This is the default and matches the
        /// SDK's historical behavior.
        /// </summary>
        AudioFilter,

        /// <summary>
        /// Read samples directly out of the microphone clip with
        /// <see cref="Microphone.GetPosition"/>/<see cref="AudioClip.GetData"/> on the main
        /// thread, without creating an AudioSource or AudioProbe.
        ///
        /// <see cref="AudioFilter"/> couples two clocks: the microphone driver writes the clip
        /// at the input device's rate while the AudioSource reads it at the output pipeline's
        /// rate. On devices where those clocks drift (measured on Meta Quest, whose capture
        /// pipeline runs at 24 kHz), the read position slides against the write position until
        /// the filter consumes stale or not-yet-written regions of the ring, which surfaces as
        /// intermittent silence and crackle. Direct polling uses the microphone's own write
        /// position as the only clock, so drift cannot accumulate; this is the same capture
        /// strategy used by Photon Voice and Mumble on the same hardware.
        /// </summary>
        DirectPolling,
    }

    sealed public class MicrophoneSource : RtcAudioSource
    {
        // DirectPolling: never emit more than this much backlog in one tick. The native audio
        // source ingests 10 ms frames into a bounded (~1 s) queue at real time; after a main
        // thread hitch (GC, scene load) Microphone.GetPosition can report hundreds of ms of
        // backlog, and pushing it all at once overflows that queue. Real-time voice cannot use
        // stale audio anyway, so the loop drops the oldest samples beyond this budget and
        // resynchronizes to the newest.
        private const int CatchUpBudgetMs = 300;

        private readonly GameObject _sourceObject;
        private readonly string _deviceName;
        private readonly MicrophoneCaptureMode _captureMode;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;
        // Invalidates an in-flight polling loop when the microphone is stopped or restarted.
        private int _pollGeneration = 0;

        /// <summary>
        /// Creates a new microphone source for the given device.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">The GameObject to attach the AudioSource to. The object must be kept in the scene
        /// for the duration of the source's lifetime.</param>
        public MicrophoneSource(string deviceName, GameObject sourceObject)
            : this(deviceName, sourceObject, MicrophoneCaptureMode.AudioFilter)
        {
        }

        /// <summary>
        /// Creates a new microphone source for the given device using the given capture mode.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">The GameObject to attach the AudioSource to (unused by
        /// <see cref="MicrophoneCaptureMode.DirectPolling"/>, which creates no components). The object must be kept
        /// in the scene for the duration of the source's lifetime.</param>
        /// <param name="captureMode">How samples are moved out of the microphone ring buffer. Use
        /// <see cref="MicrophoneCaptureMode.DirectPolling"/> on devices where the default filter-based capture
        /// exhibits drift (e.g. Meta Quest).</param>
        public MicrophoneSource(string deviceName, GameObject sourceObject, MicrophoneCaptureMode captureMode)
            : base(RtcAudioSourceType.AudioSourceMicrophone)
        {
            _deviceName = deviceName;
            _sourceObject = sourceObject;
            _captureMode = captureMode;
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
            try
            {
                clip = Microphone.Start(
                    _deviceName,
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

            if (_captureMode == MicrophoneCaptureMode.DirectPolling)
            {
                // Wait for the microphone to actually start producing data before polling.
                const float pollTimeout = 2f;
                float pollElapsed = 0f;
                while (Microphone.GetPosition(_deviceName) <= 0 && pollElapsed < pollTimeout)
                {
                    yield return new WaitForSeconds(0.05f);
                    pollElapsed += 0.05f;
                }
                if (Microphone.GetPosition(_deviceName) <= 0)
                {
                    Utils.Error($"MicrophoneSource: Microphone did not start producing data after {pollTimeout}s");
                    yield break;
                }

                int generation = ++_pollGeneration;
                MonoBehaviourContext.RunCoroutine(PollMicrophone(clip, generation));
                Utils.Debug($"MicrophoneSource device='{_deviceName}' started successfully (direct polling)");
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

            source.Play();
            Utils.Debug($"MicrophoneSource device='{_deviceName}' started successfully");
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
            _pollGeneration++; // ends an in-flight DirectPolling loop

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

        /// <summary>
        /// DirectPolling capture: reads 10 ms frames straight out of the microphone clip using
        /// the microphone's own write position as the only clock. Runs once per rendered frame;
        /// exits when the source is stopped, restarted, or the device stops recording.
        /// </summary>
        private IEnumerator PollMicrophone(AudioClip clip, int generation)
        {
            int sampleRate = clip.frequency;
            int clipChannels = clip.channels;
            int expectedChannels = (int)_expectedChannels;
            int clipLength = clip.samples;                  // samples per channel
            int frameSize = sampleRate / 100;               // 10 ms per channel
            int maxCatchUpFrames = CatchUpBudgetMs / 10;

            var readBuf = new float[frameSize * clipChannels];
            var sendBuf = clipChannels == expectedChannels ? readBuf : new float[frameSize * expectedChannels];

            int lastPos = Microphone.GetPosition(_deviceName);
            if (lastPos < 0) lastPos = 0;

            int droppedSinceWarn = 0;
            float nextWarnTime = 0f;
            bool channelMismatchWarned = false;

            while (_started && generation == _pollGeneration && Microphone.IsRecording(_deviceName))
            {
                int micPos = Microphone.GetPosition(_deviceName);
                // GetPosition can transiently return -1 on some Android device states; never
                // feed a negative offset into the modulo below or into GetData.
                if (micPos < 0)
                {
                    yield return null;
                    continue;
                }

                int available = micPos - lastPos;
                if (available < 0) available += clipLength; // ring wrap

                // A stall longer than the ring can hold makes the modulo untrustworthy (the
                // write position may have lapped us); resync to the newest data outright.
                if (available >= clipLength - frameSize)
                {
                    lastPos = micPos;
                    yield return null;
                    continue;
                }

                // Drop the oldest backlog beyond the catch-up budget so a main-thread hitch
                // can never flood the native queue in a single tick.
                int backlogFrames = available / frameSize;
                if (backlogFrames > maxCatchUpFrames)
                {
                    int dropFrames = backlogFrames - maxCatchUpFrames;
                    lastPos = (lastPos + dropFrames * frameSize) % clipLength;
                    droppedSinceWarn += dropFrames;
                }

                if (droppedSinceWarn > 0 && Time.unscaledTime >= nextWarnTime)
                {
                    Utils.Warning($"MicrophoneSource: dropped {droppedSinceWarn} stale mic frame(s) to avoid flooding the capture queue (main-thread hitch?)");
                    droppedSinceWarn = 0;
                    nextWarnTime = Time.unscaledTime + 1f;
                }

                while (_started && generation == _pollGeneration && ((micPos - lastPos + clipLength) % clipLength) >= frameSize)
                {
                    clip.GetData(readBuf, lastPos);
                    lastPos = (lastPos + frameSize) % clipLength;

                    if (clipChannels != expectedChannels)
                    {
                        if (clipChannels == 1)
                        {
                            // Mono clip, multi-channel source (the common Android case):
                            // duplicate each sample across the expected channels.
                            for (int i = 0; i < frameSize; i++)
                                for (int c = 0; c < expectedChannels; c++)
                                    sendBuf[i * expectedChannels + c] = readBuf[i];
                        }
                        else if (expectedChannels == 1)
                        {
                            // Multi-channel clip, mono source: average.
                            for (int i = 0; i < frameSize; i++)
                            {
                                float sum = 0f;
                                for (int c = 0; c < clipChannels; c++)
                                    sum += readBuf[i * clipChannels + c];
                                sendBuf[i] = sum / clipChannels;
                            }
                        }
                        else
                        {
                            // Unusual pairing: carry the first clip channel into every output
                            // channel rather than sending misinterleaved audio.
                            if (!channelMismatchWarned)
                            {
                                channelMismatchWarned = true;
                                Utils.Warning($"MicrophoneSource: clip has {clipChannels} channels but source expects {expectedChannels}; using first channel");
                            }
                            for (int i = 0; i < frameSize; i++)
                                for (int c = 0; c < expectedChannels; c++)
                                    sendBuf[i * expectedChannels + c] = readBuf[i * clipChannels];
                        }
                    }

                    try
                    {
                        AudioRead?.Invoke(sendBuf, expectedChannels, sampleRate);
                    }
                    catch (Exception e)
                    {
                        // This loop is the sole frame producer: a throwing subscriber must not
                        // kill it, or the microphone goes permanently silent.
                        if (Time.unscaledTime >= nextWarnTime)
                        {
                            Utils.Warning($"MicrophoneSource: AudioRead subscriber threw: {e.Message}");
                            nextWarnTime = Time.unscaledTime + 1f;
                        }
                    }
                }

                yield return null;
            }

            Utils.Debug($"MicrophoneSource device='{_deviceName}' polling loop ended");
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