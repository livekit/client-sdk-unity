using System;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Threading;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class AudioStream : IAudioStream
    {
        private readonly IAudioStreams audioStreams;
        private readonly FfiHandle handle;
        private readonly AudioStreamInfo info;

        private AudioFilter _audioFilter;
        private RingBuffer? _buffer;
        private short[] _tempBuffer;
        private uint _numChannels = 0;
        private uint _sampleRate;

        private AudioResampler _resampler = AudioResampler.New();
        private readonly object _lock = new();
        private readonly ConcurrentQueue<AudioStreamEvent> _pendingStreamEvents = new();

        private Thread? _writeAudioThread;

        private bool disposed;

        public AudioStream(IAudioStreams audioStreams, OwnedAudioStream ownedAudioStream)
        {
            this.audioStreams = audioStreams;
            handle = IFfiHandleFactory.Default.NewFfiHandle(ownedAudioStream.Handle!.Id);
            info = ownedAudioStream.Info!;
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            //TODO recheck
            audioStreams.Release(this);
            handle.Dispose();
            _buffer?.Dispose();
            _resampler.Dispose();

            FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
        }

        public void Start()
        {
            //TODO get rid off the thread
            _writeAudioThread = new Thread(Update);
            _writeAudioThread.Start();

            //TODO
            //_audioSource.Play();
        }

        public void ReadAudio(float[] data, int channels, int sampleRate)
        {
            lock (_lock)
            {
                if (channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    int size = (int)(channels * sampleRate * 0.2); //0.2 stands for 200 ms
                    _buffer?.Dispose();
                    _buffer = new RingBuffer(size * sizeof(short));
                    _tempBuffer = new short[data.Length];//todo avoid allocation of this buffer
                    _numChannels = (uint)channels;
                    _sampleRate = (uint)sampleRate;
                }

                static float S16ToFloat(short v)
                {
                    return v / 32768f;
                }

                // "Send" the data to Unity
                var temp = MemoryMarshal.Cast<short, byte>(_tempBuffer.AsSpan().Slice(0, data.Length));
                int read = _buffer!.Value.Read(temp);

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

            _pendingStreamEvents.Enqueue(e);
        }

        // TODO get rid this off
        private void Update()
        {
            while (true)
            {
                Thread.Sleep(Constants.TASK_DELAY);

                if (_pendingStreamEvents.TryDequeue(out var e))
                {
                    using var frame = new OwnedAudioFrame(e.FrameReceived.Frame);

                    lock (_lock)
                    {
                        using var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);
                        var data = uFrame.AsSpan();
                        _buffer?.Write(data);
                    }
                }
            }
        }
    }
}