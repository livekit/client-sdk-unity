using System;
using UnityEngine;

namespace LiveKit
{
    public class BasicAudioSource : RtcAudioSource
    {
        protected readonly AudioSource Source;
        private readonly AudioFilter _audioFilter;

        public override event Action<float[], int, int> AudioRead;

        public BasicAudioSource(AudioSource source, int channels = 2, RtcAudioSourceType sourceType = RtcAudioSourceType.AudioSourceCustom) : base(channels, sourceType)
        {
            Source = source;
            _audioFilter = Source.gameObject.AddComponent<AudioFilter>();
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            AudioRead?.Invoke(data, channels, sampleRate);
        }

        public override void Play()
        {
            _audioFilter.AudioRead += OnAudioRead;
            Source.Play();
        }

        public override void Stop()
        {
            _audioFilter.AudioRead -= OnAudioRead;
            Source.Stop();
        }
    }
}