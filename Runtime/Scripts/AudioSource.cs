using System;
using System.Collections;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LiveKit
{
    public abstract class RtcAudioSource
    {
        private AudioSource _audioSource;
        private AudioFilter _audioFilter;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;

        // Used on the AudioThread
        private AudioFrame _frame;

        public RtcAudioSource()
        {
            var newAudioSource = new NewAudioSourceRequest();
            newAudioSource.Type = AudioSourceType.AudioSourceNative;

            var request = new FFIRequest();
            request.NewAudioSource = newAudioSource;

            var resp = FfiClient.SendRequest(request);
            _info = resp.NewAudioSource.Source;
            Handle = new FfiHandle((IntPtr)_info.Handle.Id);
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
            if (_frame.NumChannels != channels 
                || _frame.SampleRate != sampleRate 
                || _frame.SamplesPerChannel != samplesPerChannel)
            {
                _frame = new AudioFrame(channels, sampleRate, samplesPerChannel);
            }

            static short FloatToS16(float v) {
                v *= 32768f;
                v = Math.Min(v, 32767f);
                v = Math.Max(v, -32768f);
                return (short)(v + Math.Sign(v) * 0.5f);
            }

            unsafe {
                fixed (float* src = data) {
                    var dst = (short*)_frame.Data.ToPointer();
                    for (var i = 0; i < data.Length; i++) {
                        dst[i] = FloatToS16(src[i]);
                    }
                }
            }

            var pushFrame = new CaptureAudioFrameRequest();
            pushFrame.SourceHandle = new FFIHandleId { Id = (ulong)Handle.DangerousGetHandle() };
            pushFrame.BufferHandle = new FFIHandleId { Id = (ulong)_frame.Handle.DangerousGetHandle() };

            var request = new FFIRequest();
            request.CaptureAudioFrame = pushFrame;

            FfiClient.SendRequest(request);
        }
    }
}
