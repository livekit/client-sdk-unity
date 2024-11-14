using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using System.Collections.Generic;

namespace LiveKit
{
    public enum RtcAudioSourceType
    {
        AudioSourceCustom = 0,
        // if the source is a microphone,
        // we don't want to play the audio locally
        AudioSourceMicrophone = 1,
    }

    public class RtcAudioSource : IRtcSource
    {
#if UNITY_IOS
        // iOS microphone sample rate is 24k,
        // please make sure when you using 
        // sourceType is AudioSourceMicrophone
        public static uint DefaultMirophoneSampleRate = 24000;

        public static uint DefaultSampleRate = 48000;
#else
        public static uint DefaultSampleRate = 48000;
        public static uint DefaultMirophoneSampleRate = DefaultSampleRate;
#endif
        public static uint DefaultChannels = 2;

        private RtcAudioSourceType _sourceType;

        public RtcAudioSourceType SourceType => _sourceType;

        private AudioSource _audioSource;
        private AudioFilter _audioFilter;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;

        // Used on the AudioThread
        private Thread _readAudioThread;
        private ThreadSafeQueue<AudioFrame> _frameQueue = new ThreadSafeQueue<AudioFrame>();

        private bool _muted = false;

        public override bool Muted => _muted;

        public RtcAudioSource(AudioSource source, RtcAudioSourceType audioSourceType = RtcAudioSourceType.AudioSourceCustom)
        {
            _sourceType = audioSourceType;

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = DefaultChannels;
            if(_sourceType == RtcAudioSourceType.AudioSourceMicrophone)
            {
                newAudioSource.SampleRate = DefaultMirophoneSampleRate;
            }
            else
            {
                newAudioSource.SampleRate = DefaultSampleRate;
            }
            newAudioSource.Options = request.TempResource<AudioSourceOptions>();
            newAudioSource.Options.EchoCancellation = true;
            newAudioSource.Options.AutoGainControl = true;
            newAudioSource.Options.NoiseSuppression = true;
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewAudioSource.Source.Info;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioSource.Source.Handle);
            UpdateSource(source);
        }

        public void Start()
        {
            Stop();
            _readAudioThread = new Thread(Update);
            _readAudioThread.Start();

            _audioFilter.AudioRead += OnAudioRead;
            while (!(Microphone.GetPosition(null) > 0)) { }
            _audioSource.Play();
        }

        public void Stop()
        {
            _readAudioThread?.Abort();
            if(_audioFilter) _audioFilter.AudioRead -= OnAudioRead;
            if(_audioSource && _audioSource.isPlaying) _audioSource.Stop();
        }

        private void Update()
        {
            while (true)
            {
                Thread.Sleep(Constants.TASK_DELAY);
                ReadAudio();
            }
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            var samplesPerChannel = data.Length / channels;
            var frame = new AudioFrame((uint)sampleRate, (uint)channels, (uint)samplesPerChannel);

            static short FloatToS16(float v)
            {
                v *= 32768f;
                v = Math.Min(v, 32767f);
                v = Math.Max(v, -32768f);
                return (short)(v + Math.Sign(v) * 0.5f);
            }

            unsafe
            {
                var frameData = new Span<short>(frame.Data.ToPointer(), frame.Length / sizeof(short));
                for (int i = 0; i < data.Length; i++)
                {
                    frameData[i] = FloatToS16(data[i]);
                }
                if (_sourceType == RtcAudioSourceType.AudioSourceMicrophone)
                {
                   // Don't play the audio locally, to avoid echo.
                    Array.Clear(data, 0, data.Length);
                }
            }
            _frameQueue.Enqueue(frame);
        }


        private void ReadAudio()
        {
            while (_frameQueue.Count > 0)
            {
                try
                {
                    AudioFrame frame = _frameQueue.Dequeue();

                    if(_muted)
                    {
                        continue;
                    }
                    unsafe
                    {
                        using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
                        using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();

                        var pushFrame = request.request;
                        pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();

                        pushFrame.Buffer = audioFrameBufferInfo;
                        pushFrame.Buffer.DataPtr = (ulong)frame.Data;
                        pushFrame.Buffer.NumChannels = frame.NumChannels;
                        pushFrame.Buffer.SampleRate = frame.SampleRate;
                        pushFrame.Buffer.SamplesPerChannel = frame.SamplesPerChannel;

                        using var response = request.Send();
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

        public override void SetMute(bool muted)
        {
            _muted = muted;
        }
    }
}
