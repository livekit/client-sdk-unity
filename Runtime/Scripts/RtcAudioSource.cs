using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using Livekit.Utils;
using System.Runtime.InteropServices;

namespace LiveKit
{
    public class RtcAudioSource : IRtcAudioSource
    {
        private const int DEFAULT_NUM_CHANNELS = 2;
        private const int DEFAULT_SAMPLE_RATE = 48000;
        private const float BUFFER_DURATION_S = 0.2f;
        private const float S16_MAX_VALUE = 32767f;
        private const float S16_MIN_VALUE = -32768f;
        private const float S16_SCALE_FACTOR = 32768f;
        private readonly Mutex<RingBuffer> buffer;
        private readonly object lockObject = new();

        private readonly AudioSource audioSource;
        private readonly IAudioFilter audioFilter;
        private short[] tempBuffer;
        private AudioFrame frame;
        private uint channels;
        private uint sampleRate;
        private int currentBufferSize;

        private int cachedFrameSize;
        public FfiHandle Handle => handle;

        internal FfiHandle handle { get; }

        public RtcAudioSource(AudioSource audioSource, IAudioFilter audioFilter)
        {
            buffer = new Mutex<RingBuffer>(new RingBuffer(0));
            currentBufferSize = 0;
            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = DEFAULT_NUM_CHANNELS;
            newAudioSource.SampleRate = DEFAULT_SAMPLE_RATE;

            using var response = request.Send();
            FfiResponse res = response;
            handle = IFfiHandleFactory.Default.NewFfiHandle(res.NewAudioSource.Source.Handle!.Id);
            this.audioSource = audioSource;
            this.audioFilter = audioFilter;
        }

        public void Start()
        {
            Stop();
            if (!audioFilter?.IsValid == true || !audioSource)
            {
                Utils.Error("AudioFilter or AudioSource is null - cannot start audio capture");
                return;
            }

            audioFilter.AudioRead += OnAudioRead;
            audioSource.Play();
        }

        public void Stop()
        {
            if (audioFilter?.IsValid == true) audioFilter.AudioRead -= OnAudioRead;
            if (audioSource) audioSource.Stop();

            lock (lockObject)
            {
                using var guard = buffer.Lock();
                guard.Value.Dispose();
                if (frame.IsValid) frame.Dispose();
            }
        }

        private void OnAudioRead(Span<float> data, int channels, int sampleRate)
        {
            var needsReconfiguration = channels != this.channels ||
                                       sampleRate != this.sampleRate ||
                                       data.Length != tempBuffer?.Length;

            var newBufferSize = 0;
            if (needsReconfiguration)
            {
                newBufferSize = (int)(channels * sampleRate * BUFFER_DURATION_S) * sizeof(short);
            }

            lock (lockObject)
            {
                if (needsReconfiguration)
                {
                    var needsNewBuffer = newBufferSize != currentBufferSize;

                    if (needsNewBuffer)
                    {
                        using var guard = buffer.Lock();
                        guard.Value.Dispose();
                        guard.Value = new RingBuffer(newBufferSize);
                        currentBufferSize = newBufferSize;
                    }

                    tempBuffer = new short[data.Length];
                    this.channels = (uint)channels;
                    this.sampleRate = (uint)sampleRate;
                    if (frame.IsValid) frame.Dispose();
                    frame = new AudioFrame(this.sampleRate, this.channels, (uint)(tempBuffer.Length / this.channels));

                    cachedFrameSize = frame.Length;
                }

                if (tempBuffer == null)
                {
                    Utils.Error("Temp buffer is null");
                    return;
                }

                var tempSpan = tempBuffer.AsSpan();
                for (var i = 0; i < data.Length; i++)
                {
                    var sample = data[i] * S16_SCALE_FACTOR;
                    if (sample > S16_MAX_VALUE) sample = S16_MAX_VALUE;
                    else if (sample < S16_MIN_VALUE) sample = S16_MIN_VALUE;
                    tempSpan[i] = (short)(sample + (sample >= 0 ? 0.5f : -0.5f));
                }

                var shouldProcessFrame = false;
                using (var guard = buffer.Lock())
                {
                    var audioBytes = MemoryMarshal.Cast<short, byte>(tempBuffer.AsSpan());
                    guard.Value.Write(audioBytes);
                    shouldProcessFrame = guard.Value.AvailableRead() >= cachedFrameSize;
                }

                if (shouldProcessFrame)
                {
                    ProcessAudioFrame();
                }
            }
        }

        private void ProcessAudioFrame()
        {
            if (!frame.IsValid) return;

            unsafe
            {
                var frameSpan = new Span<byte>(frame.Data.ToPointer(), cachedFrameSize);

                using (var guard = buffer.Lock())
                {
                    var bytesRead = guard.Value.Read(frameSpan);
                    if (bytesRead < cachedFrameSize)
                    {
                        return; // Don't send incomplete frames
                    }
                }
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
        }
    }
}