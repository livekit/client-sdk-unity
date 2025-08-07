using System;
using UnityEngine;

namespace LiveKit
{
    // from https://github.com/Unity-Technologies/com.unity.webrtc
    public class AudioFilter : MonoBehaviour, IAudioFilter
    {
        private int _sampleRate;

        private void OnEnable()
        {
            OnAudioConfigurationChanged(false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        private void OnDestroy()
        {
            AudioRead = null;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            // Called by Unity on the Audio thread
            AudioRead?.Invoke(data.AsSpan(), channels, _sampleRate);
        }

        // Event is called from the Unity audio thread
        public event IAudioFilter.OnAudioDelegate? AudioRead;

        /// <summary>
        ///     Gets whether this audio filter is valid and can be used
        /// </summary>
        public bool IsValid => this != null;

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _sampleRate = AudioSettings.outputSampleRate;
        }


        #if UNITY_EDITOR
        [ContextMenu(nameof(StartSource))]
        public void StartSource()
        {
            GetComponent<AudioSource>().Play();
        }

        [ContextMenu(nameof(StopSource))]
        public void StopSource()
        {
            GetComponent<AudioSource>().Stop();
        }

        [ContextMenu(nameof(StopSource))]
        public void LogInfo()
        {
            var source = GetComponent<AudioSource>();
            Debug.Log($"{nameof(AudioFilter)} Source: IsValid - {IsValid}, IsRecording - {source!.isPlaying}");
        }
        #endif
    }
}