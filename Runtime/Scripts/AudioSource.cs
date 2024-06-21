using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using System.Collections.Generic;

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
        private Thread _readAudioThread;
        private ThreadSafeQueue<AudioFrame> _frameQueue = new ThreadSafeQueue<AudioFrame>();

        public RtcAudioSource(AudioSource source)
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = DefaultChannels;
            newAudioSource.SampleRate = DefaultSampleRate;
            newAudioSource.Options = request.TempResource<AudioSourceOptions>();
            newAudioSource.Options.EchoCancellation = true;
            newAudioSource.Options.AutoGainControl = true;
            newAudioSource.Options.NoiseSuppression = true;
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
                // Don't play the audio locally
                Array.Clear(data, 0, data.Length);
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
    }
}
