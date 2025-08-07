using System;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Numerics;
using System.Buffers;
using LiveKit.Audio;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;

namespace LiveKit
{
    public class OptimizedMonoRtcAudioSource : IRtcAudioSource, IDisposable
    {
        private const int DEFAULT_NUM_CHANNELS = 1;
        private const int DEFAULT_SAMPLE_RATE = 48000;
        private const float S16_MAX_VALUE = 32767f;
        private const float S16_MIN_VALUE = -32768f;
        private const float S16_SCALE_FACTOR = 32768f;
        private const int POOL_SIZE = 4;
        private const int BATCH_SIZE = 4;
        private const int VECTOR_SIZE = 4;

        private readonly ConcurrentQueue<short[]> bufferPool;
        private readonly ConcurrentQueue<short[]> writeQueue;
        private readonly ConcurrentQueue<short[]> readQueue;
        private readonly object lockObject = new();

        private readonly IAudioFilter audioFilter;
        private AudioFrame frame;
        private uint channels;
        private uint sampleRate;
        private int currentBufferSize;
        private int cachedFrameSize;
        private readonly Vector4 scaleFactor;
        private readonly Vector4 maxValue;
        private readonly Vector4 minValue;
        private bool _disposed;
        private bool _muted;
        public bool Muted => _muted;

        private readonly FfiHandle handle;

        public OptimizedMonoRtcAudioSource(IAudioFilter audioFilter)
        {
            bufferPool = new ConcurrentQueue<short[]>();
            writeQueue = new ConcurrentQueue<short[]>();
            readQueue = new ConcurrentQueue<short[]>();
            currentBufferSize = 0;
            scaleFactor = new Vector4(S16_SCALE_FACTOR);
            maxValue = new Vector4(S16_MAX_VALUE);
            minValue = new Vector4(S16_MIN_VALUE);

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = DEFAULT_NUM_CHANNELS;
            newAudioSource.SampleRate = DEFAULT_SAMPLE_RATE;
            newAudioSource.Options = new AudioSourceOptions()
            {
                EchoCancellation = true,
                AutoGainControl = true,
                NoiseSuppression = true
            };

            using var response = request.Send();
            FfiResponse res = response;
            handle = IFfiHandleFactory.Default.NewFfiHandle(res.NewAudioSource.Source.Handle!.Id);
            this.audioFilter = audioFilter;
        }

        public void SetMute(bool muted)
        {
            _muted = muted;
        }

        private short[] GetBuffer(int size)
        {
            if (_disposed) return null;
            if (bufferPool.TryDequeue(out var buffer) && buffer.Length >= size)
            {
                return buffer;
            }
            return ArrayPool<short>.Shared.Rent(size);
        }

        private void ReturnBuffer(short[] buffer)
        {
            if (_disposed) return;
            if (bufferPool.Count < POOL_SIZE)
            {
                bufferPool.Enqueue(buffer);
            }
            else
            {
                ArrayPool<short>.Shared.Return(buffer);
            }
        }

        FfiHandle IRtcAudioSource.BorrowHandle()
        {
            return handle;
        }

        public void Start()
        {
            if (_disposed) return;
            Stop();
            if (!audioFilter?.IsValid == true)
            {
                return;
            }

            audioFilter.AudioRead += OnAudioRead;
        }

        public void Stop()
        {
            if (audioFilter?.IsValid == true) audioFilter.AudioRead -= OnAudioRead;

            lock (lockObject)
            {
                while (writeQueue.TryDequeue(out var buffer))
                {
                    ReturnBuffer(buffer);
                }
                while (readQueue.TryDequeue(out var buffer))
                {
                    ReturnBuffer(buffer);
                }
                if (frame.IsValid) frame.Dispose();
            }
        }

        private void ProcessVectorized(Span<float> input, Span<short> output)
        {
            if (_disposed) return;
            int i = 0;
            for (; i <= input.Length - VECTOR_SIZE; i += VECTOR_SIZE)
            {
                var vector = new Vector4(
                    input[i],
                    input[i + 1],
                    input[i + 2],
                    input[i + 3]
                );
                vector *= scaleFactor;
                vector = Vector4.Clamp(vector, minValue, maxValue);
                
                output[i] = (short)MathF.Round(vector.X);
                output[i + 1] = (short)MathF.Round(vector.Y);
                output[i + 2] = (short)MathF.Round(vector.Z);
                output[i + 3] = (short)MathF.Round(vector.W);
            }

            // Handle remaining elements
            for (; i < input.Length; i++)
            {
                var sample = input[i] * S16_SCALE_FACTOR;
                if (sample > S16_MAX_VALUE) sample = S16_MAX_VALUE;
                else if (sample < S16_MIN_VALUE) sample = S16_MIN_VALUE;
                output[i] = (short)(sample + (sample >= 0 ? 0.5f : -0.5f));
            }
        }

        private void OnAudioRead(Span<float> data, int channels, int sampleRate)
        {
            if (_disposed || _muted) return;
            var needsReconfiguration = channels != this.channels ||
                                       sampleRate != this.sampleRate;

            if (needsReconfiguration)
            {
                lock (lockObject)
                {
                    this.channels = (uint)channels;
                    this.sampleRate = (uint)sampleRate;
                    if (frame.IsValid) frame.Dispose();
                    frame = new AudioFrame(this.sampleRate, this.channels, (uint)(data.Length / this.channels));
                    cachedFrameSize = frame.LengthBytes();
                }
            }

            var buffer = GetBuffer(data.Length);
            if (buffer == null) return;
            
            ProcessVectorized(data, buffer);
            writeQueue.Enqueue(buffer);

            if (writeQueue.Count >= BATCH_SIZE)
            {
                ProcessAudioFrames();
            }
        }

        private void ProcessAudioFrames()
        {
            if (!frame.IsValid || _disposed || _muted) return;

            while (writeQueue.TryDequeue(out var buffer))
            {
                readQueue.Enqueue(buffer);
            }

            var framesToProcess = Math.Min(readQueue.Count, BATCH_SIZE);
            var frameBuffers = new short[framesToProcess][];

            for (int i = 0; i < framesToProcess; i++)
            {
                if (readQueue.TryDequeue(out var buffer))
                {
                    frameBuffers[i] = buffer;
                }
            }

            for (int i = 0; i < framesToProcess; i++)
            {
                var buffer = frameBuffers[i];
                unsafe
                {
                    var frameSpan = new Span<byte>(frame.Data.ToPointer(), cachedFrameSize);
                    var audioBytes = MemoryMarshal.Cast<short, byte>(buffer.AsSpan());
                    audioBytes.Slice(0, Math.Min(audioBytes.Length, cachedFrameSize)).CopyTo(frameSpan);
                }

                try
                {
                    using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
                    using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();

                    var pushFrame = request.request;
                    pushFrame.SourceHandle = (ulong)handle.DangerousGetHandle();
                    pushFrame.Buffer = audioFrameBufferInfo;
                    pushFrame.Buffer.DataPtr = (ulong)frame.Data;
                    pushFrame.Buffer.NumChannels = frame.NumChannels;
                    pushFrame.Buffer.SampleRate = frame.SampleRate;
                    pushFrame.Buffer.SamplesPerChannel = frame.SamplesPerChannel;

                    using var response = request.Send();

                    pushFrame.Buffer.DataPtr = 0;
                    pushFrame.Buffer.NumChannels = 0;
                    pushFrame.Buffer.SampleRate = 0;
                    pushFrame.Buffer.SamplesPerChannel = 0;
                }
                catch (Exception e)
                {
                    Utils.Error("Audio Framedata error: " + e.Message + "\nStackTrace: " + e.StackTrace);
                }

                ReturnBuffer(buffer);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Stop();
                if (frame.IsValid) frame.Dispose();
            }
            _disposed = true;
        }

        ~OptimizedMonoRtcAudioSource()
        {
            Dispose(false);
        }
    }
}