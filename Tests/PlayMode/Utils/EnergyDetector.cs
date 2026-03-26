using System;
using UnityEngine;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// MonoBehaviour that detects audio energy via OnAudioFilterRead.
    /// Attach to the same GameObject as an AudioSource after AudioStream
    /// has added its AudioProbe, so this filter runs second and sees
    /// the filled audio data.
    /// </summary>
    public class EnergyDetector : MonoBehaviour
    {
        /// <summary>
        /// Fired on the audio thread when non-trivial energy is detected.
        /// The double parameter is the RMS energy of the frame.
        /// </summary>
        public event Action<double> EnergyDetected;

        void OnAudioFilterRead(float[] data, int channels)
        {
            double sumSquared = 0.0;
            for (int i = 0; i < data.Length; i++)
            {
                sumSquared += data[i] * data[i];
            }
            double rms = Math.Sqrt(sumSquared / data.Length);

            if (rms > 0.01)
                EnergyDetected?.Invoke(rms);
        }
    }
}
