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
        private AudioSource _audioSource;
        private AudioFilter _audioFilter;

        internal readonly FfiOwnedHandle Handle;
        protected AudioSourceInfo _info;

        // Used on the AudioThread
        private AudioFrame _frame;
        private Thread _readAudioThread;
        private object _lock = new object();
        private float[] _data;
        private int _channels;
        private int _sampleRate;
        private volatile bool _pending = false;

        public RtcAudioSource(AudioSource source)
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = 2;
            newAudioSource.SampleRate = 48000;
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewAudioSource.Source.Info;
            //TODO pooling handles
            Handle = res.NewAudioSource.Source.Handle;
            UpdateSource(source);
        }

        public void Start()
        {
            Stop();
            _readAudioThread = new Thread(Update);
            _readAudioThread.Start();
        }

        public void Stop()
        {
            if (_readAudioThread != null)
            {
                _readAudioThread.Abort();
                _readAudioThread = null;
            }
        }

        private void Update()
        {
            while (true)
            {
                Thread.Sleep(Constants.TASK_DELAY);
                if (_pending)
                {
                    ReadAudio();
                }
            }
        }

        private void ReadAudio()
        {
            _pending = false;
            lock (_lock)
            {
                var samplesPerChannel = _data.Length / _channels;
                if (_frame == null
                    || _frame.NumChannels != _channels
                    || _frame.SampleRate != _sampleRate
                    || _frame.SamplesPerChannel != samplesPerChannel)
                {
                    _frame = new AudioFrame(_sampleRate, _channels, samplesPerChannel);
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
                        for (int i = 0; i < _data.Length; i++)
                        {
                            frameData[i] = FloatToS16(_data[i]);
                        }

                        using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
                        var pushFrame = request.request;
                        pushFrame.SourceHandle = (ulong)Handle.Id;
                        pushFrame.Buffer = new AudioFrameBufferInfo
                        {
                            DataPtr = (ulong)_frame.Data,
                            NumChannels = _frame.NumChannels,
                            SampleRate = _frame.SampleRate,
                            SamplesPerChannel = _frame.SamplesPerChannel
                        };
                        using var response = request.Send();
                    }

                    Utils.Debug($"Pushed audio frame with {_data.Length} sample rate "
                                + _frame.SampleRate
                                + "  num channels "
                                + _frame.NumChannels
                                + " and samplers per channel "
                                + _frame.SamplesPerChannel);
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
            //_audioFilter.hideFlags = HideFlags.HideInInspector;
            _audioFilter.AudioRead += OnAudioRead;
            source.Play();
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            lock (_lock)
            {
                _data = data;
                _channels = channels;
                _sampleRate = sampleRate;
                _pending = true;
            }
        }
    }
}
