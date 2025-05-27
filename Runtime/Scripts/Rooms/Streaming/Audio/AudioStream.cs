using System;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using Livekit.Utils;
using UnityEngine;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class AudioStream : IAudioStream
    {
        private readonly IAudioStreams audioStreams;
        private readonly IAudioRemixConveyor audioRemixConveyor;
        private readonly FfiHandle handle;
        private readonly AudioStreamInfo info;

        private IAudioFilter _audioFilter;
        private Mutex<RingBuffer>? _buffer;
        private short[] _tempBuffer;
        private uint _numChannels = 0;
        private uint _sampleRate;

        private readonly object _lock = new();

        private bool disposed;

        public AudioStream(
            IAudioStreams audioStreams,
            OwnedAudioStream ownedAudioStream,
            IAudioRemixConveyor audioRemixConveyor
        )
        {
            this.audioStreams = audioStreams;
            this.audioRemixConveyor = audioRemixConveyor;
            handle = IFfiHandleFactory.Default.NewFfiHandle(ownedAudioStream.Handle!.Id);
            info = ownedAudioStream.Info!;
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            handle.Dispose();
            if (_buffer != null)
            {
                using var guard = _buffer.Lock();
                guard.Value.Dispose();
            }

            FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
            audioStreams.Release(this);
        }

        public void ReadAudio(float[] data, int channels, int sampleRate)
        {
            lock (_lock)
            {
                if (channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    int size = (int)(channels * sampleRate * 0.2); //0.2 stands for 200 ms
                    if (_buffer != null)
                    {
                        using var guard = _buffer.Lock();
                        guard.Value.Dispose();
                    }

                    _buffer = new Mutex<RingBuffer>(new RingBuffer(size * sizeof(short)));

                    _tempBuffer = new short[data.Length]; //todo avoid allocation of this buffer
                    _numChannels = (uint)channels;
                    _sampleRate = (uint)sampleRate;
                }

                static float S16ToFloat(short v)
                {
                    return v / 32768f;
                }

                // "Send" the data to Unity
                var temp = MemoryMarshal.Cast<short, byte>(_tempBuffer.AsSpan().Slice(0, data.Length));

                {
                    using var guard = _buffer!.Lock();
                    int read = guard.Value.Read(temp);
                }

                Array.Clear(data, 0, data.Length);
                for (int i = 0; i < data.Length; i++) data[i] = S16ToFloat(_tempBuffer[i]);
            }
        }

        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if (e.StreamHandle != (ulong)handle.DangerousGetHandle())
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            if (_numChannels == 0)
                return;

            if (_buffer == null)
            {
                Debug.LogError("Invalid case, buffer is not set yet");
                // prevent leak
                var tempHandle = IFfiHandleFactory.Default.NewFfiHandle(e.FrameReceived.Frame.Handle.Id);
                tempHandle.Dispose();
                return;
            }

            var frame = new OwnedAudioFrame(e.FrameReceived.Frame);
            audioRemixConveyor.Process(frame, _buffer, _numChannels, _sampleRate);
        }
    }
}