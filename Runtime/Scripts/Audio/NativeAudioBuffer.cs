using System;
using LiveKit.Types;
using RichTypes;
using UnityEngine;

namespace LiveKit.Audio
{
    /// <summary>
    /// Implementation is not thread safe
    /// </summary>
    public struct NativeAudioBuffer : IDisposable
    {
        private readonly uint bufferDurationMs;
        private NativeRingBuffer<PCMSample> buffer;
        private uint channels;
        private uint sampleRate;

        /// <summary>
        /// Initializes a new audio sample buffer for holding samples for a given duration.
        /// </summary>
        public NativeAudioBuffer(uint bufferDurationMs = 200)
        {
            this.bufferDurationMs = bufferDurationMs;
            buffer = default;
            channels = 0;
            sampleRate = 0;
        }

        public void Write<TFrame>(TFrame frame) where TFrame : IAudioFrame
        {
            Write(frame.AsPCMSampleSpan(), frame.NumChannels, frame.SampleRate);
        }

        public void Write(ReadOnlySpan<PCMSample> samples, uint channels, uint sampleRate)
        {
            Capture(samples, channels, sampleRate);
        }

        private void Capture(ReadOnlySpan<PCMSample> data, uint channels, uint sampleRate)
        {
            if (channels != this.channels || sampleRate != this.sampleRate)
            {
                buffer.Dispose();
                var rawSize = (int)(channels * sampleRate * (bufferDurationMs / 1000f));
                IntPowerOf2 size = IntPowerOf2.NewOrNextPowerOf2(rawSize);
                buffer = new NativeRingBuffer<PCMSample>(size);

                this.channels = channels;
                this.sampleRate = sampleRate;
            }

            buffer.Enqueue(data);
        }

        public Option<AudioFrame> Read(uint sampleRate, uint numChannels, uint samplesPerChannel)
        {
            if (this.sampleRate == 0)
            {
                Debug.LogError("Buffer: attempt to read from an initialized buffer");
                return Option<AudioFrame>.None;
            }

            if (sampleRate != this.sampleRate)
            {
                Debug.LogError($"Buffer: sample rate doesn't match: required - {sampleRate}, real - {this.sampleRate}");
                return Option<AudioFrame>.None;
            }

            if (channels == 0 || sampleRate == 0) return Option<AudioFrame>.None;

            uint lengthToRead = numChannels * samplesPerChannel;
            Span<PCMSample> read = buffer.TryDequeue((int)lengthToRead, out bool success);

            if (success)
            {
                var frame = new AudioFrame(sampleRate, numChannels, samplesPerChannel);
                read.CopyTo(frame.AsPCMSampleSpan());
                return Option<AudioFrame>.Some(frame);
            }

            return Option<AudioFrame>.None;
        }

        public void Dispose()
        {
            buffer.Dispose();
        }
    }
}