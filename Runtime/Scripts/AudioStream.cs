using System;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Rooms;
using LiveKit.Rooms.Tracks;

namespace LiveKit
{
    public class AudioStream
    {
        internal FfiHandle Handle => _handle;

        private FfiHandle _handle;
        private AudioSource _audioSource;
        private AudioFilter _audioFilter;
        private RingBuffer? _buffer;
        private short[] _tempBuffer;
        private uint _numChannels;
        private uint _sampleRate;
        private AudioResampler _resampler;
        private object _lock = new object();


        private Thread? _readAudioThread;
        private bool _pending = false;

        public AudioStream(ITrack audioTrack, AudioSource source)
        {
            if (audioTrack.Kind is not TrackKind.KindAudio)
                throw new InvalidOperationException("audioTrack is not an audio track");
            
            if (!audioTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("audiotrack's room is invalid");

            if (!audioTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("audiotrack's participant is invalid");

            _resampler = new AudioResampler();

            using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
            var newAudioStream = request.request;
            newAudioStream.TrackHandle = (ulong)room.Handle.DangerousGetHandle();
            newAudioStream.Type = AudioStreamType.AudioStreamNative;
            using var response = request.Send();
            FfiResponse res = response;
            var streamInfo = res.NewAudioStream.Stream;

            _handle = IFfiHandleFactory.Default.NewFfiHandle(streamInfo.Handle.Id);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;

            UpdateSource(source);
        }

        private void UpdateSource(AudioSource source)
        {
            _audioSource = source;
            _audioFilter = source.gameObject.AddComponent<AudioFilter>();
            //_audioFilter.hideFlags = HideFlags.HideInInspector;
            _audioFilter.AudioRead += OnAudioRead;
            source.Play();
        }


        private float[] _data;
        private int _channels;
        private int _pendingSampleRate;

        // Called on Unity audio thread
        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            lock (_lock)
            {
                _data = data;
                _channels = channels;
                _pendingSampleRate = sampleRate;
                _pending = true;
            }
        }

        public void Start()
        {
            Stop();
            _readAudioThread = new Thread(Update);
            _readAudioThread.Start();
        }

        public void Stop()
        {
            _readAudioThread?.Abort();
        }

        private void Update()
        {
            while (true)
            {
                Thread.Sleep(Constants.TASK_DELAY);

                if (_pending)
                {
                    lock (_lock)
                    {
                        _pending = false;
                        if (_buffer == null || _channels != _numChannels || _pendingSampleRate != _sampleRate || _data.Length != _tempBuffer.Length)
                        {
                            int size = (int)(_channels * _sampleRate * 0.2);
                            _buffer?.Dispose();
                            _buffer = new RingBuffer(size * sizeof(short));
                            _tempBuffer = new short[_data.Length];
                            _numChannels = (uint)_channels;
                            _sampleRate = (uint)_pendingSampleRate;
                        }


                        static float S16ToFloat(short v)
                        {
                            return v / 32768f;
                        }

                        // "Send" the data to Unity
                        //var temp = MemoryMarshal.Cast<short, byte>(_tempBuffer.AsSpan().Slice(0, _data.Length));
                        //int read = _buffer.Read(temp);

                        Array.Clear(_data, 0, _data.Length);
                        for (int i = 0; i < _data.Length; i++)
                        {
                            _data[i] = S16ToFloat(_tempBuffer[i]);
                        }
                    }
                }
            }
        }


        // Called on the MainThread (See FfiClient)
        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if (e.StreamHandle != (ulong)Handle.DangerousGetHandle())
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var info = e.FrameReceived.Frame.Info;
            var handle = IFfiHandleFactory.Default.NewFfiHandle(e.FrameReceived.Frame.Handle.Id);
            var frame = new AudioFrame(handle, info);

            lock (_lock)
            {
                if (_numChannels == 0)
                    return;


                unsafe
                {
                    var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);
                    var data = new Span<byte>(uFrame.Data.ToPointer(), uFrame.Length);
                    _buffer?.Write(data);
                }
            }
        }
    }
}
