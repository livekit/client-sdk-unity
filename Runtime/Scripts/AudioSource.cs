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

        //internal readonly FfiHandle Handle;
        private FfiHandle _handle;
        internal FfiHandle Handle
        {
            get { return _handle; }
        }
        protected AudioSourceInfo _info;

        // Used on the AudioThread
        private AudioFrame _frame;
        private Thread _readAudioThread;
        private bool _pending = false;
        private object _lock = new object();

        public RtcAudioSource(AudioSource source)
        {
            var newAudioSource = new NewAudioSourceRequest();
            newAudioSource.Type = AudioSourceType.AudioSourceNative;

            var request = new FfiRequest();
            request.NewAudioSource = newAudioSource;

            var resp = FfiClient.SendRequest(request);

            _info = resp.NewAudioSource.Source.Info;
            _handle = new FfiHandle((IntPtr)resp.NewAudioSource.Source.Handle.Id);
            UpdateSource(source);

        }

        public void Start()
        {
            Stop();
            _readAudioThread = new Thread(async () => await Update());
            _readAudioThread.Start();
        }

        public void Stop()
        {
            if (_readAudioThread != null) _readAudioThread.Abort();
        }

        async private Task Update()
        {
            while(true)
            {
                await Task.Delay(Constants.TASK_DELAY);
                if(_pending)
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
                if (_frame == null || _frame.NumChannels != _channels
                    || _frame.SampleRate != _sampleRate
                    || _frame.SamplesPerChannel != samplesPerChannel)
                {
                    _frame = new AudioFrame(_sampleRate, _channels, samplesPerChannel);
                }

                try
                {
                    // Don't play the audio locally
                    Array.Clear(_data, 0, _data.Length);

                    var pushFrame = new CaptureAudioFrameRequest();
                    pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();
                    pushFrame.Buffer = new AudioFrameBufferInfo() { DataPtr = (ulong)_frame.Handle.DangerousGetHandle() };

                    var request = new FfiRequest();
                    request.CaptureAudioFrame = pushFrame;

                    FfiClient.SendRequest(request);

                    Utils.Debug($"Pushed audio frame with {_data.Length} samples");

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

        private float[] _data;
        private int _channels;
        private int _sampleRate;

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
