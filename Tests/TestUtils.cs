using System;

namespace LiveKit.Tests
{
    internal static class TestUtils
    {
        /// <summary>
        /// Generates a sine wave with the specified parameters.
        /// </summary>
        /// <param name="channels">Number of audio channels.</param>
        /// <param name="sampleRate">Sample rate in Hz.</param>
        /// <param name="durationMs">Duration in milliseconds.</param>
        /// <param name="frequency">Frequency of the sine wave in Hz.</param>
        /// <returns>A float array containing the generated sine wave.</returns>
        internal static float[] GenerateSineWave(uint channels, uint sampleRate, uint durationMs, uint frequency = 440)
        {
            var samplesPerChannel = sampleRate * durationMs / 1000;
            var samples = new float[samplesPerChannel * channels];
            for (int i = 0; i < samplesPerChannel; i++)
            {
                float sampleValue = (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
                for (int channel = 0; channel < channels; channel++)
                    samples[i * channels + channel] = sampleValue;
            }
            return samples;
        }
    }
}

