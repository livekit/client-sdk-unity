using System;
using UnityEngine;

namespace LiveKit
{
    public interface IAudioFilter
    {
        /// <summary>
        ///     Event delegate for audio data processing
        /// </summary>
        /// <param name="data">Audio sample data</param>
        /// <param name="channels">Number of audio channels</param>
        /// <param name="sampleRate">Sample rate of the audio</param>
        delegate void OnAudioDelegate(Span<float> data, int channels, int sampleRate);

        /// <summary>
        ///     Gets whether this audio filter is valid and can be used
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        ///     Event called from the Unity audio thread when audio data is available
        /// </summary>
        event OnAudioDelegate AudioRead;
    }
}