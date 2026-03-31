using System;
using System.Diagnostics;
using UnityEngine;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// MonoBehaviour that identifies audio pulses by frequency using the Goertzel algorithm.
    /// Attach to the same GameObject as an AudioSource after AudioStream has added its
    /// AudioProbe, so this filter runs second and sees the filled audio data.
    /// </summary>
    public class AudioPulseDetector : MonoBehaviour
    {
        public int TotalPulses;
        public double BaseFrequency;
        public double FrequencyStep;
        public double MagnitudeThreshold;

        /// <summary>
        /// Fired on the audio thread when a pulse is detected.
        /// Parameters: (pulseIndex, magnitude).
        /// </summary>
        public event Action<long, int, double> PulseReceived;

        private int _sampleRate;

        void OnEnable()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigChanged;
        }

        void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigChanged;
        }

        void OnAudioConfigChanged(bool deviceWasChanged)
        {
            _sampleRate = AudioSettings.outputSampleRate;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            long receiveTimeTicks = Stopwatch.GetTimestamp();
            int sampleRate = _sampleRate;
            int samples = data.Length / channels;
            if (samples == 0) return;

            int bestPulse = -1;
            double bestMag = 0;

            for (int p = 0; p < TotalPulses; p++)
            {
                double freq = BaseFrequency + p * FrequencyStep;
                double mag = Goertzel(data, channels, samples, sampleRate, freq);
                if (mag > bestMag)
                {
                    bestMag = mag;
                    bestPulse = p;
                }
            }

            // Debug.Log($"bestPulse: {bestPulse} | bestMag: {bestMag}");
            if (bestPulse >= 0 && bestMag > MagnitudeThreshold)
                PulseReceived?.Invoke(receiveTimeTicks, bestPulse, bestMag);
        }

        /// <summary>
        /// Goertzel algorithm — computes the magnitude of a single frequency bin.
        /// O(N) per frequency, much cheaper than a full FFT.
        /// </summary>
        static double Goertzel(float[] data, int channels, int N, int sampleRate, double freq)
        {
            double k = 0.5 + (double)N * freq / sampleRate;
            double w = 2.0 * Math.PI * k / N;
            double coeff = 2.0 * Math.Cos(w);
            double s0 = 0, s1 = 0, s2 = 0;

            for (int i = 0; i < N; i++)
            {
                s0 = data[i * channels] + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }

            double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            return Math.Sqrt(Math.Abs(power)) / N;
        }
    }
}
