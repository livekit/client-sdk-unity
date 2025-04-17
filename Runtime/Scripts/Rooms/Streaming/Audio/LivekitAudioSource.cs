using System;
using UnityEngine;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class LivekitAudioSource : MonoBehaviour
    {
        private static ulong counter;

        private int sampleRate;
        private WeakReference<IAudioStream>? stream;
        private AudioSource audioSource;

        public static LivekitAudioSource New(bool explicitName = false)
        {
            var gm = new GameObject();
            var source = gm.AddComponent<LivekitAudioSource>();
            source.audioSource = gm.AddComponent<AudioSource>();
            if (explicitName) source.name = $"{nameof(LivekitAudioSource)}_{counter++}";
            return source;
        }

        public void Construct(WeakReference<IAudioStream> audioStream)
        {
            stream = audioStream;
        }

        public void Free()
        {
            stream = null;
        }

        public void Play()
        {
            audioSource.Play();
        }

        public void Stop()
        {
            audioSource.Stop();
        }

        public void SetVolume(float target)
        {
            audioSource.volume = target;
        }

        private void OnEnable()
        {
            OnAudioConfigurationChanged(false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            sampleRate = AudioSettings.outputSampleRate;
        }

        // Called by Unity on the Audio thread
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (stream != null && stream.TryGetTarget(out var s))
            {
                s?.ReadAudio(data, channels, sampleRate);
            }
        }
    }
}