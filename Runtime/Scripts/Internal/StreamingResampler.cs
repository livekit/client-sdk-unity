using System;
using System.Collections.Generic;

namespace LiveKit.Internal
{
    /// <summary>
    /// Streaming linear resampler for mono audio. Interpolation state carries across chunks, so a
    /// stream processed in arbitrary slices produces the same output as processing it whole.
    /// Free of UnityEngine dependencies so it can be unit tested.
    /// </summary>
    internal sealed class StreamingResampler
    {
        private readonly double _step; // input samples advanced per output sample
        private double _pos;           // fractional read position; >= -1, where -1 maps to _prev
        private float _prev;           // last sample of the previous chunk

        public StreamingResampler(int inputRate, int outputRate)
        {
            if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
            if (outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputRate));
            _step = (double)inputRate / outputRate;
        }

        public void Reset()
        {
            _pos = 0.0;
            _prev = 0f;
        }

        /// <summary>
        /// Resamples the first <paramref name="count"/> samples of <paramref name="input"/> and
        /// returns the produced output samples (possibly empty for very small chunks).
        /// </summary>
        public float[] Process(float[] input, int count)
        {
            if (count <= 0) return Array.Empty<float>();

            var output = new List<float>((int)(count / _step) + 2);
            double pos = _pos;
            while (pos < count - 1)
            {
                int i0 = (int)Math.Floor(pos);
                float a = i0 < 0 ? _prev : input[i0];
                float b = input[i0 + 1];
                float frac = (float)(pos - i0);
                output.Add(a * (1f - frac) + b * frac);
                pos += _step;
            }
            _prev = input[count - 1];
            _pos = pos - count;
            return output.ToArray();
        }
    }
}
