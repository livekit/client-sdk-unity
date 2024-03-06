using UnityEngine;

namespace LiveKit
{
    // from https://github.com/Unity-Technologies/com.unity.webrtc
    internal class AudioFilter : MonoBehaviour
    {
        public delegate void OnAudioDelegate(float[] data, int channels, int sampleRate);
        // Event is called from the Unity audio thread
        public event OnAudioDelegate AudioRead;
        private int _sampleRate;

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
