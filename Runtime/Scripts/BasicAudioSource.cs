using System;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// An audio source which captures from a Unity <see cref="AudioSource"/> in the scene.
    /// </summary>
    sealed public class BasicAudioSource : RtcAudioSource
    {
        private readonly AudioSource _source;

        public override event Action<float[], int, int> AudioRead;

        private bool _disposed = false;
        private bool _started = false;

        /// <summary>
        /// Creates a new basic audio source for the given <see cref="AudioSource"/> in the scene.
        /// </summary>
        /// <param name="source">The <see cref="AudioSource"/> to capture from.</param>
        /// <param name="channels">The number of channels to capture.</param>
        /// <param name="sourceType">The type of audio source.</param>
        public BasicAudioSource(AudioSource source, int channels = 2, RtcAudioSourceType sourceType = RtcAudioSourceType.AudioSourceCustom) : base(channels, sourceType)
        {
            _source = source;
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            AudioRead?.Invoke(data, channels, sampleRate);
        }

        public override void Start()
        {
            base.Start();
            if (_started) return;

            var probe = _source.gameObject.AddComponent<AudioProbe>();
            probe.AudioRead += OnAudioRead;

            _source.Play();
            _started = true;
        }

        public override void Stop()
        {
            base.Stop();
            if (!_started) return;

            var probe = _source.gameObject.GetComponent<AudioProbe>();
            UnityEngine.Object.Destroy(probe);

            _source.Stop();
            _started = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing) Stop();
            _disposed = true;
            base.Dispose(disposing);
        }

        ~BasicAudioSource()
        {
            Dispose(false);
        }
    }
}