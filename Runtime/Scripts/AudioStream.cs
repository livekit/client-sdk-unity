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
        private RingBuffer _buffer;
        private short[] _tempBuffer;
        private uint _numChannels;
        private uint _sampleRate;
        private AudioResampler _resampler = new AudioResampler();
        private object _lock = new object();
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
            var probe = _audioSource.gameObject.AddComponent<AudioProbe>();
            probe.AudioRead += OnAudioRead;
            _audioSource.Play();
        }

        // Called on Unity audio thread
        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
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
            if ((ulong)Handle.DangerousGetHandle() != e.StreamHandle)
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            lock (_lock)
            {
                if (_numChannels == 0)
                    return;

                unsafe
                {
                    // Change deal with issue in newer LiveKit where channels are returned incorrectly
                    // -https://github.com/livekit/client-sdk-unity/issues/169
                    // (plus some new changes to reduce garbage creation)
                    if (e.FrameReceived.Frame.Info.NumChannels == 1 && _numChannels == 2)
                    {

                        int samplesPerChannel = (int)e.FrameReceived.Frame.Info.SamplesPerChannel;
                        int monoLengthBytes =
                            (int)(e.FrameReceived.Frame.Info.SamplesPerChannel *
                                  e.FrameReceived.Frame.Info.NumChannels *
                                  sizeof(short));

                        // Span over the incoming mono audio bytes
                        var monoByteSpan = new ReadOnlySpan<byte>(
                            (void*)e.FrameReceived.Frame.Info.DataPtr,
                            monoLengthBytes);

                        // Treat them as 16-bit samples
                        var monoSamples = MemoryMarshal.Cast<byte, short>(monoByteSpan);

                        // Allocate stereo buffer on the stack: 2 channels * samples * sizeof(short)
                        Span<short> stereoSamples = stackalloc short[samplesPerChannel * 2];

                        for (int i = 0; i < samplesPerChannel; i++)
                        {
                            short sample = monoSamples[i]; // mono
                            int dstIndex = i * 2;

                            stereoSamples[dstIndex] = sample; // Left
                            stereoSamples[dstIndex + 1] = sample; // Right
                        }

                        // Cast the stereo short span to bytes and write to the buffer
                        _buffer?.Write(MemoryMarshal.AsBytes(stereoSamples));
                    }
                    else
                    {
                        //TODO add support here if they fix the above bug
                    }
                }
            }
            // Change - need to drop handle here because it would normally be done
            // within AudioFrame,  without memory will be leaked on the plugin side
            NativeMethods.FfiDropHandle((IntPtr)e.FrameReceived.Frame.Handle.Id);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
                _audioSource.Stop();
                UnityEngine.Object.Destroy(_audioSource.GetComponent<AudioProbe>());
                _buffer?.Dispose();
            }
            _disposed = true;
        }

        ~AudioStream()
        {
            Dispose(false);
        }
    }
}
