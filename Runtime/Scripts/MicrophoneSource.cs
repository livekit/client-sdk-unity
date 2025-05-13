using System;
using UnityEngine;

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
        private readonly string _deviceName;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;

        /// <summary>
        /// True indicates the capture has started but is temporarily suspended
        /// due to the application entering the background.
        /// </summary>
        private bool _suspended = false;

        /// <summary>
        /// Creates a new microphone source for the given device.
        /// </summary>
        /// <param name="deviceName">The name of the device to capture from. Use <see cref="Microphone.devices"/> to
        /// get the list of available devices.</param>
        /// <param name="sourceObject">The GameObject to attach the AudioSource to. The object must be kept in the scene
        /// for the duration of the source's lifetime.</param>
        public MicrophoneSource(string deviceName, GameObject sourceObject) : base(2, RtcAudioSourceType.AudioSourceMicrophone)
        {
            _deviceName = deviceName;
            _sourceObject = sourceObject;
            MonoBehaviourContext.OnApplicationPauseEvent += OnApplicationPause;
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

            var clip = Microphone.Start(
                _deviceName,
                loop: true,
                lengthSec: 1,
                frequency: (int)DefaultMicrophoneSampleRate
            );
            if (clip == null)
                throw new InvalidOperationException("Microphone start failed");

            var source = _sourceObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;

            var probe = _sourceObject.AddComponent<AudioProbe>();
            probe.AudioRead += OnAudioRead;

            var waitUntilReady = new WaitUntil(() => Microphone.GetPosition(_deviceName) > 0);
            MonoBehaviourContext.RunCoroutine(waitUntilReady, () => source.Play());

            _started = true;
        }

        /// <summary>
        /// Stops capturing audio from the microphone.
        /// </summary>
        public override void Stop()
        {
            base.Stop();
            if (!_started) return;

            if (Microphone.IsRecording(_deviceName))
                Microphone.End(_deviceName);

            var probe = _sourceObject.GetComponent<AudioProbe>();
            probe.AudioRead -= OnAudioRead;
            UnityEngine.Object.Destroy(probe);

            var source = _sourceObject.GetComponent<AudioSource>();
            UnityEngine.Object.Destroy(source);

            _started = false;
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            AudioRead?.Invoke(data, channels, sampleRate);
            // Don't play the audio locally, to avoid echo.
            data.AsSpan().Clear();
        }

        private void OnApplicationPause(bool pause)
        {
            // When the application is paused (i.e. enters the background), place
            // the microphone capture in a suspended state. This prevents stale audio
            // samples from being captured and sent to the server when the application
            // is resumed.
            if (_suspended && !pause)
            {
                Start();
                _suspended = false;
            }
            else if (!_suspended && pause)
            {
                Stop();
                _suspended = true;
            }
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