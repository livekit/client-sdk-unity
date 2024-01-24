using System;
using System.Collections;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading.Tasks;
using System.Threading;

namespace LiveKit
{
    public class RtcAudioSource
    {
        private AudioSource _audioSource;
        private AudioFilter _audioFilter;

        private FfiHandle _handle;

        internal FfiHandle Handle => _handle;

        protected AudioSourceInfo _info;

        // Used on the AudioThread
        private AudioFrame _frame;
        private Thread _readAudioThread;
        private object _lock = new object();
        private float[] _data;
        private int _channels;
        private int _sampleRate;
        private volatile bool _pending = false;

        public RtcAudioSource(AudioSource source, AudioFilter audioFilter)
        {
            var newAudioSource = new NewAudioSourceRequest();
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            //newAudioSource.NumChannels = 2;
            //newAudioSource.SampleRate = 48000;

            var request = new FfiRequest();
            request.NewAudioSource = newAudioSource;

            var resp = FfiClient.SendRequest(request);
            _info = resp.NewAudioSource.Source.Info;
            _handle = new FfiHandle((IntPtr)resp.NewAudioSource.Source.Handle.Id);
            UpdateSource(source, audioFilter);
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

                        var pushFrame = new CaptureAudioFrameRequest();
                        pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();
                        pushFrame.Buffer = new AudioFrameBufferInfo()
                        {
                            DataPtr = (ulong)_frame.Data, NumChannels = _frame.NumChannels, SampleRate = _frame.SampleRate,
                            SamplesPerChannel = _frame.SamplesPerChannel
                        };
                        var request = new FfiRequest();
                        request.CaptureAudioFrame = pushFrame;

                        FfiClient.SendRequest(request);
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

        private void UpdateSource(AudioSource source, AudioFilter audioFilter)
        {
            _audioSource = source;
            _audioFilter = audioFilter;
            _audioFilter.AudioRead += OnAudioRead;
            _audioSource.Play();
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