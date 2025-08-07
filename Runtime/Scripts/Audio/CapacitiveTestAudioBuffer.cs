using System;
using RichTypes;
using UnityEngine;

namespace LiveKit.Audio
{
    [Obsolete("Has been used only for tests, saved as a reference")]
    public class CapacitiveTestAudioBuffer : IDisposable
    {
        private readonly uint bufferDurationMs;
        private PCMSample[]? buffer;
        private uint channels;
        private uint sampleRate;

        private int nextIndex;

        private int nextRead;

        /// <summary>
        /// Initializes a new audio sample buffer for holding samples for a given duration.
        /// </summary>
        internal CapacitiveTestAudioBuffer(uint bufferDurationMs = 10_000)
        {
            this.bufferDurationMs = bufferDurationMs;
            buffer = null;
            channels = 0;
            sampleRate = 0;
            nextIndex = 0;
            nextRead = 0;
        }

        internal void Write(ReadOnlySpan<PCMSample> samples, uint channels, uint sampleRate)
        {
            Capture(samples, channels, sampleRate);
        }

        private void Capture(ReadOnlySpan<PCMSample> data, uint channels, uint sampleRate)
        {
            if (channels != this.channels || sampleRate != this.sampleRate)
            {
                var size = (int)(channels * sampleRate * (bufferDurationMs / 1000f));
                buffer = new PCMSample[size];
                this.channels = channels;
                this.sampleRate = sampleRate;
                nextIndex = 0;
            }

            var length = data.Length;

            var remainingLength = buffer.Length - nextIndex;
            //Drop overwhelming samples
            var targetLength = Math.Min(length, remainingLength);
            targetLength = Math.Clamp(targetLength, 0, targetLength);

            Span<PCMSample> slice = buffer.AsSpan().Slice(nextIndex, targetLength);
            data.Slice(0, targetLength).CopyTo(slice);
            nextIndex += targetLength;
            Debug.Log("Buffer: write");
        }


        private bool IsBufferFilled()
        {
            if (buffer == null) return false;
            return nextIndex >= buffer.Length;
        }

        private void ResetBuffer()
        {
            nextIndex = 0;
            nextRead = 0;
            Debug.Log("Buffer: reset");
        }

        internal Option<AudioFrame> Read(uint sampleRate, uint numChannels, uint samplesPerChannel)
        {
            if (sampleRate != this.sampleRate)
            {
                Debug.LogError($"Buffer: sample rate doesn't match: required - {sampleRate}, real - {this.sampleRate}");
                return Option<AudioFrame>.None;
            }
            
            if (channels == 0 || sampleRate == 0) return Option<AudioFrame>.None;

            if (IsBufferFilled())
            {
                Debug.Log("Buffer: read");
                var availableSampleCount = buffer!.Length - nextRead;

                if (availableSampleCount <= 0)
                {
                    ResetBuffer();
                    return Option<AudioFrame>.None;
                }

                var targetSampleCount = numChannels * samplesPerChannel;
                var frame = new AudioFrame(sampleRate, numChannels, samplesPerChannel);

                var toRead = Math.Min(availableSampleCount, targetSampleCount);
                Span<PCMSample> sliceToReadFrom = buffer.AsSpan().Slice(nextRead, (int)toRead);
                Span<PCMSample> targetSlice = frame.AsPCMSampleSpan();
                
                //Silence
                targetSlice.Fill(new PCMSample(0));
                
                sliceToReadFrom.CopyTo(targetSlice);

                nextRead += (int)toRead;
                return Option<AudioFrame>.Some(frame);
            }

            return Option<AudioFrame>.None;
        }

        public void Dispose()
        {
            buffer = null;
        }
    }
}