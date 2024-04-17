using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    public class RtcAudioSource
    {
        public static uint DefaultSampleRate = 48000;
        public static uint DefaultChannels = 2;

        private AudioSource _audioSource;
        private AudioFilter _audioFilter;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;

        // Used on the AudioThread
        private AudioFrame _frame;
        private object _lock = new object();

        public RtcAudioSource(AudioSource source)
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = DefaultChannels;
            newAudioSource.SampleRate = DefaultSampleRate;

            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewAudioSource.Source.Info;
            //TODO pooling handles
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioSource.Source.Handle);
            UpdateSource(source);
        }

        public void Start()
        {
            Stop();
            _audioFilter.AudioRead += OnAudioRead;
            _audioSource.Play();
        }

        public void Stop()
        {
            if(_audioFilter) _audioFilter.AudioRead -= OnAudioRead;
            if(_audioSource && _audioSource.isPlaying) _audioSource.Stop();
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            lock (_lock)
            {
                var samplesPerChannel = data.Length / channels;
                if (_frame == null
                    || _frame.NumChannels != channels
                    || _frame.SampleRate != sampleRate
                    || _frame.SamplesPerChannel != samplesPerChannel)
                {
                    _frame = new AudioFrame((uint)sampleRate, (uint)channels, (uint)samplesPerChannel);
                }

                try
                {

                    static short FloatToS16(float v)
                    {
                        v *= 32768f;
                        v = Math.Min(v, 32767f);
                        v = Math.Max(v, -32768f);
                        return (short)(v + Math.Sign(v) * 0.5f);
                    }

                    unsafe
                    {
                        var frameData = new Span<short>(_frame.Data.ToPointer(), _frame.Length / sizeof(short));
                        for (int i = 0; i < data.Length; i++)
                        {
                            frameData[i] = FloatToS16(data[i]); 
                        }

                        // Don't play the audio locally
                        Array.Clear(data, 0, data.Length);

                        using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
                        using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();
                        
                        var pushFrame = request.request;
                        pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();
  
                        pushFrame.Buffer = audioFrameBufferInfo;
                        pushFrame.Buffer.DataPtr = (ulong)_frame.Data;
                        pushFrame.Buffer.NumChannels = _frame.NumChannels;
                        pushFrame.Buffer.SampleRate = _frame.SampleRate;
                        pushFrame.Buffer.SamplesPerChannel = _frame.SamplesPerChannel;

                        using var response = request.Send();

                        pushFrame.Buffer.DataPtr = 0;
                        pushFrame.Buffer.NumChannels = 0;
                        pushFrame.Buffer.SampleRate = 0;
                        pushFrame.Buffer.SamplesPerChannel = 0;

                    }
                }
                catch (Exception e)
                {
                    Utils.Error("Audio Framedata error: " + e.Message);
                }
            }
        }

        private void UpdateSource(AudioSource source)
        {
            _audioSource = source;
            _audioFilter = source.gameObject.AddComponent<AudioFilter>();
        }
    }
}
