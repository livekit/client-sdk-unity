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
        private SynchronizationContext syncContext;

        private RtcAudioSource()
        {
            syncContext = SynchronizationContext.Current;
        }

        async static public Task<RtcAudioSource> Create(AudioSource source, CancellationToken canceltoken)
        {
            var response = new RtcAudioSource();
            await response.Init(source, canceltoken);
            if (canceltoken.IsCancellationRequested) return null;
            return response;
        }

        async public Task Init(AudioSource source, CancellationToken canceltoken)
        {
            if (canceltoken.IsCancellationRequested) return;

            var newAudioSource = new NewAudioSourceRequest();
            newAudioSource.Type = AudioSourceType.AudioSourceNative;

            var request = new FfiRequest();
            request.NewAudioSource = newAudioSource;

            var resp = await FfiClient.SendRequest(request);
            // Check if the task has been cancelled
            if (canceltoken.IsCancellationRequested) return ;

            _info = resp.NewAudioSource.Source.Info;
            _handle = new FfiHandle((IntPtr)resp.NewAudioSource.Source.Handle.Id);
            UpdateSource(source);
            return;
        }

        private void UpdateSource(AudioSource source)
        {
            syncContext.Post(_ =>
            {
                _audioSource = source;
                _audioFilter = source.gameObject.AddComponent<AudioFilter>();
                //_audioFilter.hideFlags = HideFlags.HideInInspector;
                _audioFilter.AudioRead += OnAudioRead;
                source.Play();
            }, null);
            
        }


        private async void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            var samplesPerChannel = data.Length / channels;
            if (_frame == null || _frame.NumChannels != channels 
                || _frame.SampleRate != sampleRate 
                || _frame.SamplesPerChannel != samplesPerChannel)
            {
                _frame = new AudioFrame(sampleRate, channels, samplesPerChannel);
            }

            try
            {
                await Task.Run(async () =>
                {
                    // Don't play the audio locally
                    Array.Clear(data, 0, data.Length);

                    var pushFrame = new CaptureAudioFrameRequest();
                    pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();
                    pushFrame.Buffer = new AudioFrameBufferInfo() { DataPtr = (ulong)_frame.Handle.DangerousGetHandle() };

                    var request = new FfiRequest();
                    request.CaptureAudioFrame = pushFrame;

                    await FfiClient.SendRequest(request);

                    Utils.Debug($"Pushed audio frame with {data.Length} samples");
                });
                
            }
            catch (Exception e)
            {

                Utils.Error("Audio Framedata error: "+e.Message);
            }
           

           
        }
    }
}
