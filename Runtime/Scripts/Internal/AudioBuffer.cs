using System;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    /// A ring buffer for audio samples.
    /// </summary>
    internal class AudioBuffer
    {
        private readonly uint _bufferDurationMs;
        private RingBuffer _buffer;
        private uint _channels;
        private uint _sampleRate;

        /// <summary>
        /// Initializes a new audio sample buffer for holding samples for a given duration.
        /// </summary>
        internal AudioBuffer(uint bufferDurationMs = 200)
        {
            _bufferDurationMs = bufferDurationMs;
        }

        /// <summary>
        /// Write audio samples.
        /// </summary>
        /// <remarks>
        /// The float data will be converted to short format before being written to the buffer.
        /// If the number of channels or sample rate changes, the buffer will be recreated.
        /// </remarks>
        /// <param name="data">The audio samples to write.</param>
        /// <param name="channels">The number of channels in the audio data.</param>
        /// <param name="sampleRate">The sample rate of the audio data in Hz.</param>
        internal void Write(float[] data, uint channels, uint sampleRate)
        {
            static short FloatToS16(float v)
            {
                v *= 32768f;
                v = Math.Min(v, 32767f);
                v = Math.Max(v, -32768f);
                return (short)(v + Math.Sign(v) * 0.5f);
            }

            var s16Data = new short[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                s16Data[i] = FloatToS16(data[i]);
            }
            Capture(s16Data, channels, sampleRate);
        }

        private void Capture(short[] data, uint channels, uint sampleRate)
        {
            if (_buffer == null || channels != _channels || sampleRate != _sampleRate)
            {
                var size = (int)(channels * sampleRate * (_bufferDurationMs / 1000f));
                _buffer?.Dispose();
                _buffer = new RingBuffer(size * sizeof(short));
                _channels = channels;
                _sampleRate = sampleRate;
            }
            unsafe
            {
                fixed (short* pData = data)
                {
                    var byteData = new ReadOnlySpan<byte>(pData, data.Length * sizeof(short));
                    _buffer.Write(byteData);
                }
            }
        }

        /// <summary>
        /// Reads a frame that is the length of the given duration.
        /// </summary>
        /// <param name="durationMs">The duration of the audio samples to read in milliseconds.</param>
        /// <returns>An AudioFrame containing the read audio samples or if there is not enough samples, null.</returns>
        internal AudioFrame ReadDuration(uint durationMs)
        {
            if (_buffer == null) return null;

            var samplesForDuration = (uint)(_sampleRate * (durationMs / 1000f));
            var requiredLength = samplesForDuration * _channels * sizeof(short);
            if (_buffer.AvailableRead() < requiredLength) return null;

            var frame = new AudioFrame(_sampleRate, _channels, samplesForDuration);
            unsafe
            {
                var frameData = new Span<byte>(frame.Data.ToPointer(), frame.Length);
                _buffer.Read(frameData);
            }
            return frame;
        }
    }
}