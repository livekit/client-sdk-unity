using System;
using LiveKit.Internal;
using RichTypes;

namespace LiveKit.Audio
{
    public class AudioBuffer : IDisposable
    {
        private readonly uint bufferDurationMs;
        private RingBuffer buffer;
        private uint channels;
        private uint sampleRate;

        /// <summary>
        /// Initializes a new audio sample buffer for holding samples for a given duration.
        /// </summary>
        internal AudioBuffer(uint bufferDurationMs = 200)
        {
            this.bufferDurationMs = bufferDurationMs;
            buffer = new RingBuffer(0);
            channels = 0;
            sampleRate = 0;
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
        internal void Write(ReadOnlySpan<float> data, uint channels, uint sampleRate)
        {
            // TODO reuse temp buffer
            var s16Data = new PCMSample[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                s16Data[i] = PCMSample.FromUnitySample(data[i]);
            }

            Capture(s16Data, channels, sampleRate);
        }

        private void Capture(PCMSample[] data, uint channels, uint sampleRate)
        {
            if (channels != this.channels || sampleRate != this.sampleRate)
            {
                var size = (int)(channels * sampleRate * (bufferDurationMs / 1000f));
                buffer.Dispose();
                buffer = new RingBuffer(size * sizeof(short));
                this.channels = channels;
                this.sampleRate = sampleRate;
            }

            unsafe
            {
                fixed (PCMSample* pData = data)
                {
                    var byteData = new ReadOnlySpan<byte>(pData, data.Length * sizeof(PCMSample));
                    buffer.Write(byteData);
                }
            }
        }

        /// <summary>
        /// Reads a frame that is the length of the given duration.
        /// </summary>
        /// <param name="durationMs">The duration of the audio samples to read in milliseconds.</param>
        /// <returns>An AudioFrame containing the read audio samples or if there is not enough samples, null.</returns>
        internal Option<AudioFrame> ReadDuration(uint durationMs)
        {
            if (channels == 0 || sampleRate == 0) return Option<AudioFrame>.None;

            var samplesForDuration = (uint)(sampleRate * (durationMs / 1000f));
            var requiredLength = samplesForDuration * channels * sizeof(short);
            if (buffer.AvailableRead() < requiredLength) return Option<AudioFrame>.None;

            var frame = new AudioFrame(sampleRate, channels, samplesForDuration);
            unsafe
            {
                var frameData = new Span<byte>(frame.Data.ToPointer(), frame.Length);
                buffer.Read(frameData);
            }

            return Option<AudioFrame>.Some(frame);
        }

        public void Dispose()
        {
            buffer.Dispose();
        }
    }
}