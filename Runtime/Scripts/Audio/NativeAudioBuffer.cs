using System;
using System.IO;
using Livekit.Types;
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
                Debug.LogWarning("Buffer: attempt to read from an initialized buffer");
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

    public struct NativeAudioBufferResampleTee : IDisposable
    {
        private NativeAudioBuffer buffer;
        private WavWriter beforeResampleWriter;
        private WavWriter afterResampleWriter;

        public readonly bool IsWavActive => beforeResampleWriter.IsDisposed() == false
                                            && afterResampleWriter.IsDisposed() == false;

        public NativeAudioBufferResampleTee(
            NativeAudioBuffer buffer,
            WavWriter beforeResampleWriter,
            WavWriter afterResampleWriter)
        {
            this.buffer = buffer;
            this.beforeResampleWriter = beforeResampleWriter;
            this.afterResampleWriter = afterResampleWriter;
        }

        public Option<AudioFrame> Read(uint sampleRate, uint numChannels, uint samplesPerChannel)
        {
            return buffer.Read(sampleRate, numChannels, samplesPerChannel);
        }


        public void Write<TFrame>(TFrame frame) where TFrame : IAudioFrame
        {
            buffer.Write(frame);
        }

        public void Write(ReadOnlySpan<PCMSample> samples, uint channels, uint sampleRate)
        {
            buffer.Write(samples, channels, sampleRate);
        }

        public void TryWriteWavTee<TFrameBefore, TFrameAfter>(TFrameBefore beforeFrame, TFrameAfter afterFrame)
            where TFrameBefore : IAudioFrame
            where TFrameAfter : IAudioFrame
        {
            if (beforeResampleWriter.IsDisposed() == false)
            {
                beforeResampleWriter.Write(beforeFrame);
            }

            if (afterResampleWriter.IsDisposed() == false)
            {
                afterResampleWriter.Write(afterFrame);
            }
        }

        public void TryWavTeeBeforeFrame(ReadOnlySpan<PCMSample> samples, uint channels, uint sampleRate)
        {
            if (beforeResampleWriter.IsDisposed() == false)
            {
                beforeResampleWriter.Write(samples, channels, sampleRate);
            }
        }

        public void TryWavTeeAfterFrame<TFrame>(TFrame frame) where TFrame : IAudioFrame
        {
            if (afterResampleWriter.IsDisposed() == false)
            {
                afterResampleWriter.Write(frame);
            }
        }

        public Result StartWavTeeToDisk(string beforeWavWriterFilePath, string afterWavWriterFilePath)
        {
            if (beforeResampleWriter.IsDisposed() == false || afterResampleWriter.IsDisposed() == false)
            {
                return Result.ErrorResult("Already writing");
            }

            Result<WavWriter> beforeWriterResult = WavWriter.NewFromPath(beforeWavWriterFilePath);
            if (beforeWriterResult.Success == false)
                return beforeWriterResult;

            Result<WavWriter> afterWriterResult = WavWriter.NewFromPath(afterWavWriterFilePath);
            if (afterWriterResult.Success == false)
            {
                WavWriter beforeWriter = beforeWriterResult.Value;
                beforeWriter.Dispose();
                File.Delete(beforeWavWriterFilePath);
                return afterWriterResult;
            }

            beforeResampleWriter = beforeWriterResult.Value;
            afterResampleWriter = afterWriterResult.Value;

            return Result.SuccessResult();
        }

        public Result StopWavTeeToDisk()
        {
            if (beforeResampleWriter.IsDisposed() || afterResampleWriter.IsDisposed())
            {
                return Result.ErrorResult("Already disposed");
            }

            beforeResampleWriter.Dispose();
            afterResampleWriter.Dispose();
            return Result.SuccessResult();
        }

        public void Dispose()
        {
            buffer.Dispose();
            if (beforeResampleWriter.IsDisposed() == false)
            {
                beforeResampleWriter.Dispose();
            }

            if (afterResampleWriter.IsDisposed() == false)
            {
                afterResampleWriter.Dispose();
            }
        }
    }

    public readonly struct WavTeeControl
    {
        private readonly Mutex<NativeAudioBufferResampleTee> mutex;
        private readonly string beforeWavFilePath;
        private readonly string afterWavFilePath;

        public bool IsWavActive
        {
            get
            {
                using Mutex<NativeAudioBufferResampleTee>.Guard guard = mutex.Lock();
                return guard.Value.IsWavActive;
            }
        }

        public WavTeeControl(Mutex<NativeAudioBufferResampleTee> mutex, string beforeWavFilePath, string afterWavFilePath)
        {
            this.mutex = mutex;
            this.beforeWavFilePath = beforeWavFilePath;
            this.afterWavFilePath = afterWavFilePath;
        }

        public Result Toggle()
        {
            using Mutex<NativeAudioBufferResampleTee>.Guard guard = mutex.Lock();
            return guard.Value.IsWavActive
                ? guard.Value.StopWavTeeToDisk()
                : guard.Value.StartWavTeeToDisk(beforeWavFilePath, afterWavFilePath);
        }

        public Result StartWavTeeToDisk()
        {
            using Mutex<NativeAudioBufferResampleTee>.Guard guard = mutex.Lock();
            return guard.Value.StartWavTeeToDisk(beforeWavFilePath, afterWavFilePath);
        }

        public Result StopWavTeeToDisk()
        {
            using Mutex<NativeAudioBufferResampleTee>.Guard guard = mutex.Lock();
            return guard.Value.StopWavTeeToDisk();
        }
    }
}