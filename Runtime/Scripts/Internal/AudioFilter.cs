using UnityEngine;

namespace LiveKit
{
    // from https://github.com/Unity-Technologies/com.unity.webrtc
    public class AudioFilter : MonoBehaviour, IAudioFilter
    {
        // Event is called from the Unity audio thread
        public event IAudioFilter.OnAudioDelegate AudioRead;
        private int _sampleRate;

        /// <summary>
        /// Gets whether this audio filter is valid and can be used
        /// </summary>
        public bool IsValid => this != null;

        void OnEnable()
        {
            OnAudioConfigurationChanged(false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _sampleRate = AudioSettings.outputSampleRate;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            // Called by Unity on the Audio thread
            AudioRead?.Invoke(data, channels, _sampleRate);
        }

        private void OnDestroy()
        {
            AudioRead = null;
        }
    }
}
