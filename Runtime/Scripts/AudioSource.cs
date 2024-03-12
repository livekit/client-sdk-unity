using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;

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

        public RtcAudioSource(AudioSource source)
        {
            var newAudioSource = new NewAudioSourceRequest();
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = 2;
            newAudioSource.SampleRate = 48000;

            var request = new FfiRequest();
            request.NewAudioSource = newAudioSource;

            var resp = FfiClient.SendRequest(request);
            var respSource = resp.NewAudioSource.Source;
            _info = respSource.Info;

            Handle = respSource.Handle;
            UpdateSource(source);
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
            var samplesPerChannel = data.Length / channels;
            if (_frame == null || _frame.NumChannels != channels 
                || _frame.SampleRate != sampleRate 
                || _frame.SamplesPerChannel != samplesPerChannel)
            {
                _frame = new AudioFrame(sampleRate, channels, samplesPerChannel);
            }

            static short FloatToS16(float v) {
                v *= 32768f;
                v = Math.Min(v, 32767f);
                v = Math.Max(v, -32768f);
                return (short)(v + Math.Sign(v) * 0.5f);
            }

            unsafe {
                var frameData = new Span<short>(_frame.Data.ToPointer(), _frame.Length / sizeof(short));
                for (int i = 0; i < data.Length; i++)
                {
                    frameData[i] = FloatToS16(data[i]);
                }
            }

            // Don't play the audio locally
            Array.Clear(data, 0, data.Length);

            var audioFrameBufferInfo = new AudioFrameBufferInfo();

            audioFrameBufferInfo.DataPtr = (ulong)_frame.Data;
            audioFrameBufferInfo.NumChannels = _frame.NumChannels;
            audioFrameBufferInfo.SampleRate = _frame.SampleRate;
            audioFrameBufferInfo.SamplesPerChannel = _frame.SamplesPerChannel;

            var pushFrame = new CaptureAudioFrameRequest();
            pushFrame.SourceHandle = Handle.Id;
            pushFrame.Buffer = audioFrameBufferInfo;

            var request = new FfiRequest();
            request.CaptureAudioFrame = pushFrame;

            FfiClient.SendRequest(request);


            //Debug.Log($"Pushed audio frame with {data.Length} samples");
        }
    }
}
