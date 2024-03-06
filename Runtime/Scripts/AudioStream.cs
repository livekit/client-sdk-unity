using System;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using LiveKit.Rooms.Tracks;

namespace LiveKit
{

    public class AudioStream
    {
        //internal readonly FfiHandle Handle;
        private FfiHandle _handle;
        internal FfiHandle Handle
        {
            get { return _handle; }
        }
        private AudioSource _audioSource;
        private AudioFilter _audioFilter;
        private RingBuffer _buffer;
        private short[] _tempBuffer;
        private uint _numChannels = 0;
        private uint _sampleRate;
        private AudioResampler _resampler = new AudioResampler();
        private object _lock = new object();
        private Queue<AudioStreamEvent> _pendingStreamEvents = new Queue<AudioStreamEvent>();

        private Thread? _writeAudioThread;
        private bool _playing = false;

        public AudioStream(ITrack audioTrack, AudioSource source)
        {
            if (audioTrack.Kind is not TrackKind.KindAudio)
                throw new InvalidOperationException("audioTrack is not an audio track");

            if (!audioTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("audiotrack's room is invalid");

            if (!audioTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("audiotrack's participant is invalid");

            using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
            var newAudioStream = request.request;
            newAudioStream.TrackHandle = (ulong)audioTrack.Handle.DangerousGetHandle();
            newAudioStream.Type = AudioStreamType.AudioStreamNative;
            using var response = request.Send();
            FfiResponse res = response;
            var streamInfo = res.NewAudioStream.Stream;

            _handle = IFfiHandleFactory.Default.NewFfiHandle((IntPtr)streamInfo.Handle.Id);

            UpdateSource(source);
        }

        private void UpdateSource(AudioSource source)
        {
            _audioSource = source;
            _audioFilter = source.gameObject.AddComponent<AudioFilter>();
        }

        public void Start()
        {
            Stop();
            _playing = true;
            _writeAudioThread = new Thread(Update);
            _writeAudioThread.Start();

            _audioFilter.AudioRead += OnAudioRead;
            _audioSource.Play();

            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;
        }

        public void Stop()
        {
            _playing = false;
            _writeAudioThread?.Abort();

            if (_audioFilter)
                _audioFilter.AudioRead -= OnAudioRead;
            if (_audioSource) _audioSource.Stop();

            if (FfiClient.Instance != null)
                FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
        }

        // Called on Unity audio thread
        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            lock (_lock)
            {
                if (channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    int size = (int)(channels * sampleRate * .2f);
                    _buffer = new RingBuffer(size * sizeof(short));
                    _tempBuffer = new short[data.Length];
                    _numChannels = (uint)channels;
                    _sampleRate = (uint)sampleRate;
                }

                static float S16ToFloat(short v)
                {
                    return v / 32768f;
                }

                // "Send" the data to Unity
                var temp = MemoryMarshal.Cast<short, byte>(_tempBuffer.AsSpan().Slice(0, data.Length));
                int read = _buffer.Read(temp);
                Array.Clear(data, 0, data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = S16ToFloat(_tempBuffer[i]);
                }
            }
        }



        // Called on the MainThread (See FfiClient)
        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if (!_playing) return;

            if (e.StreamHandle != (ulong)Handle.DangerousGetHandle())
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            if (_numChannels == 0)
                return;

            _pendingStreamEvents!.Enqueue(e);
        }

        private void Update()
        {
            while (true)
            {
                Thread.Sleep(Constants.TASK_DELAY);

                if (_pendingStreamEvents != null && _pendingStreamEvents.Count > 0)
                {
                    var e = _pendingStreamEvents.Dequeue();

                    var info = e.FrameReceived.Frame.Info;
                    var frame = new AudioFrame(info);

                    lock (_lock)
                    {
                        unsafe
                        {
                            var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);
                            var data = new Span<byte>(uFrame.Data.ToPointer(), uFrame.Length);
                            _buffer.Write(data);
                        }
                    }

                }
            }
        }
    }
}
