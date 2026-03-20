using System;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    /// <summary>
    /// An audio stream from a remote participant, attached to an <see cref="AudioSource"/>
    /// in the scene.
    /// </summary>
    public sealed class AudioStream : IDisposable
    {
        internal readonly FfiHandle Handle;
        private readonly AudioSource _audioSource;
        private readonly AudioProbe _probe;
        private RingBuffer _buffer;
        private short[] _tempBuffer;
        private uint _numChannels;
        private uint _sampleRate;
        private AudioResampler _resampler = new AudioResampler();
        private readonly object _lock = new object();
        private bool _disposed = false;

        /// <summary>
        /// Creates a new audio stream from a remote audio track, attaching it to the
        /// given <see cref="AudioSource"/> in the scene.
        /// </summary>
        /// <param name="audioTrack">The remote audio track to stream.</param>
        /// <param name="source">The audio source to play the stream on.</param>
        /// <exception cref="InvalidOperationException">Thrown if the audio track's room or
        /// participant is invalid.</exception>
        public AudioStream(RemoteAudioTrack audioTrack, AudioSource source)
        {
            if (!audioTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("audiotrack's room is invalid");

            if (!audioTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("audiotrack's participant is invalid");

            using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
            var newAudioStream = request.request;
            newAudioStream.TrackHandle = (ulong)(audioTrack as ITrack).TrackHandle.DangerousGetHandle();
            newAudioStream.Type = AudioStreamType.AudioStreamNative;

            using var response = request.Send();
            FfiResponse res = response;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioStream.Stream.Handle);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;

            _audioSource = source;
            _probe = _audioSource.gameObject.AddComponent<AudioProbe>();
            _probe.AudioRead += OnAudioRead;
            _audioSource.Play();
        }

        // Called on Unity audio thread
        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (_disposed)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            lock (_lock)
            {
                if (_buffer == null || channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    int size = (int)(channels * sampleRate * 0.2);
                    _buffer?.Dispose();
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
            if (_disposed)
                return;

            if ((ulong)Handle.DangerousGetHandle() != e.StreamHandle)
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            using var frame = new AudioFrame(e.FrameReceived.Frame);

            lock (_lock)
            {
                if (_numChannels == 0)
                    return;

                unsafe
                {
                    using var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);
                    if (uFrame != null)
                    {
                        var data = new Span<byte>(uFrame.Data.ToPointer(), uFrame.Length);
                        _buffer?.Write(data);
                    }

                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            // Remove long-lived delegate references first so this instance can become collectible
            // as soon as user code drops it. This also prevents late native callbacks from
            // touching partially disposed state.
            FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;

            lock (_lock)
            {
                // Native resources can be released on both the explicit-dispose and finalizer
                // paths. Unity objects are only touched when Dispose() is called explicitly on the
                // main thread.
                if (disposing)
                {
                    _audioSource.Stop();
                    if (_probe != null)
                    {
                        _probe.AudioRead -= OnAudioRead;
                        UnityEngine.Object.Destroy(_probe);
                    }
                }

                _buffer?.Dispose();
                _buffer = null;
                _tempBuffer = null;
                _resampler?.Dispose();
                _resampler = null;
                Handle.Dispose();
            }

            _disposed = true;
        }

        ~AudioStream()
        {
            Dispose(false);
        }
    }
}
