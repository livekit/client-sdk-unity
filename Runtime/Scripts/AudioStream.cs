using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;

namespace LiveKit
{
    // from https://github.com/Unity-Technologies/com.unity.webrtc
    internal class AudioFilter : MonoBehaviour
    {
        public delegate void OnAudioDelegate(float[] data, int channels, int sampleRate);
        // Event is called from the Unity audio thread
        public event OnAudioDelegate AudioRead;

        private int _sampleRate;

        void OnEnable()
        {
            OnAudioConfigurationChanged(false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _sampleRate = AudioSettings.outputSampleRate;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            // Called by Unity on the Audio thread
            AudioRead?.Invoke(data, channels, _sampleRate);
        }
    }

    public class AudioStream
    {
        internal readonly FfiHandle Handle;
        private AudioSource _audioSource;
        private AudioFilter _audioFilter;
        private RingBuffer _buffer;

        public AudioStream(IAudioTrack audioTrack, AudioSource source)
        {
            if (!audioTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("audiotrack's room is invalid");

            if (!audioTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("audiotrack's participant is invalid");

            var newAudioStream = new NewAudioStreamRequest();
            newAudioStream.RoomHandle = new FFIHandleId { Id = (ulong)room.Handle.DangerousGetHandle() };
            newAudioStream.ParticipantSid = participant.Sid;
            newAudioStream.TrackSid = audioTrack.Sid;
            newAudioStream.Type = AudioStreamType.AudioStreamNative;

            var request = new FFIRequest();
            request.NewAudioStream = newAudioStream;

            var resp = FfiClient.SendRequest(request);
            var streamInfo = resp.NewAudioStream.Stream;

            Handle = new FfiHandle((IntPtr)streamInfo.Handle.Id);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;

            UpdateSource(source);
        }

        private void UpdateSource(AudioSource source)
        {
            _audioSource = source;
            _audioFilter = source.gameObject.AddComponent<AudioFilter>();
            _audioFilter.hideFlags = HideFlags.HideInInspector;
            _audioFilter.AudioRead += OnAudioRead;
        }

        // Called on Unity audio thread
        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            lock (_buffer)
            {

            }
        }

        // Called on the MainThread (See FfiClient)
        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if (e.Handle.Id != (ulong)Handle.DangerousGetHandle())
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var info = e.FrameReceived.Frame;
            var handle = new FfiHandle((IntPtr)info.Handle.Id);
            unsafe
            {
                uint len = info.SamplesPerChannel * info.NumChannels;
                var data = new Span<byte>(((IntPtr)info.DataPtr).ToPointer(), (int)len);

                lock (_buffer)
                {
                    if (_buffer.AvailableWrite() < len)
                    {
                        Utils.Error("AudioStream buffer overflow");
                        return;
                    }
                    _buffer.Write(data);
                }
            }
        }
    }
}
