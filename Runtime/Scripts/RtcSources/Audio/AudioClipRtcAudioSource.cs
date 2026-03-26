using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Streaming.Audio;
using Livekit.Types;
using RichTypes;
using UnityEngine;
using UnityEngine.Audio;

namespace LiveKit.Audio
{
    public class AudioClipRtcAudioSource : IRtcAudioSource
    {
        private const int DEFAULT_NUM_CHANNELS = 2;

        private readonly AudioResampler audioResampler = AudioResampler.New();
        private readonly Mutex<NativeAudioBuffer> buffer = new(new NativeAudioBuffer(200));

        private readonly AudioFilter audioFilter;
        private readonly AudioSource audioSource;

        private bool handleBorrowed;
        private bool disposed;

        private volatile float audioVolumeLinear;
        private readonly CancellationTokenSource audioVolumeLoopCancellationTokenSource;

        private readonly FfiHandle handle;

        public bool IsRecording => audioFilter.IsValid;

        private AudioClipRtcAudioSource(
            AudioFilter audioFilter,
            AudioSource audioSource,
            (AudioMixer audioMixer, string audioVolumeParameter)? audioMixerVolume)
        {
            this.audioFilter = audioFilter;
            this.audioSource = audioSource;

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = DEFAULT_NUM_CHANNELS;
            newAudioSource.SampleRate = SampleRate.Hz48000.valueHz;

            using var options = request.TempResource<AudioSourceOptions>();
            newAudioSource.Options = options;
            newAudioSource.Options.EchoCancellation = true;
            newAudioSource.Options.NoiseSuppression = true;
            newAudioSource.Options.AutoGainControl = true;

            using var response = request.Send();
            FfiResponse res = response;
            handle = IFfiHandleFactory.Default.NewFfiHandle(res.NewAudioSource.Source.Handle!.Id);

            audioVolumeLoopCancellationTokenSource = new CancellationTokenSource();
            audioVolumeLinear = 1; //max
            if (audioMixerVolume.HasValue)
            {
                VolumePollLoopAsync(
                    audioMixerVolume.Value.audioMixer!,
                    audioMixerVolume.Value.audioVolumeParameter!,
                    audioVolumeLoopCancellationTokenSource.Token
                ).Forget();
            }
        }

        public static AudioClipRtcAudioSource New(
            AudioClip clip,
            (AudioMixer audioMixer, string audioVolumeParameter)? audioMixerVolume = null)
        {
            GameObject gm = new GameObject(nameof(AudioClipRtcAudioSource));
            AudioSource audioSource = gm.AddComponent<AudioSource>()!;
            audioSource.clip = clip;
            audioSource.Play();
            AudioFilter filter = gm.AddComponent<AudioFilter>()!;
            gm.AddComponent<OmitAudioFilter>();

            return new AudioClipRtcAudioSource(filter, audioSource, audioMixerVolume);
        }

        FfiHandle IRtcAudioSource.BorrowHandle()
        {
            if (handleBorrowed)
            {
                Utils.Error("Borrowing already borrowed handle, may cause undefined behaviour");
            }

            handleBorrowed = true;
            return handle;
        }

        private async UniTaskVoid VolumePollLoopAsync(
            AudioMixer audioMixer,
            string parameterName,
            CancellationToken token)
        {
            while (token.IsCancellationRequested == false)
            {
                if (audioMixer.GetFloat(parameterName, out float volumeDb))
                {
                    audioVolumeLinear = Mathf.Pow(10f, volumeDb / 20f);
                }

                await UniTask.Delay(50, cancellationToken: token).SuppressCancellationThrow(); // ~20Hz
            }
        }

        public void Start()
        {
            Stop();
            if (audioFilter.IsValid == false)
            {
                Utils.Error("AudioFilter or AudioSource is null - cannot start audio capture");
                return;
            }

            audioFilter.AudioRead += OnAudioRead;
            audioSource.Play();
        }

        public void Stop()
        {
            if (audioFilter.IsValid) audioFilter.AudioRead -= OnAudioRead;
            audioSource.Stop();
        }

        public void Toggle()
        {
            if (IsRecording)
                Stop();
            else
                Start();
        }

        private void OnAudioRead(Span<float> data, int channels, int sampleRate)
        {
            using var guard = buffer.Lock();

            PCMSample[] converted = new PCMSample[data.Length];

            // cache to don't access volatile variable for each sample 
            var volume = audioVolumeLinear;

            for (int i = 0; i < data.Length; i++)
            {
                var sample = data[i] * volume;
                converted[i] = PCMSample.FromUnitySample(sample);
            }

            guard.Value.Write(converted, (uint)channels, (uint)sampleRate);
            while (true)
            {
                var ms10ToRead = sampleRate / 100;
                Option<AudioFrame> frameResult = guard.Value.Read((uint)sampleRate, (uint)channels, (uint)ms10ToRead);
                if (frameResult.Has == false) break;
                using AudioFrame rawFrame = frameResult.Value;
                using OwnedAudioFrame frame =
                    audioResampler.LiveKitCompatibleRemixAndResample(rawFrame, DEFAULT_NUM_CHANNELS);

                ProcessAudioFrame(frame);
            }
        }

        private void ProcessAudioFrame(in OwnedAudioFrame frame)
        {
            try
            {
                using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
                using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();

                var pushFrame = request.request;
                pushFrame.SourceHandle = (ulong)handle.DangerousGetHandle();
                pushFrame.Buffer = audioFrameBufferInfo;
                pushFrame.Buffer.DataPtr = (ulong)frame.Data;
                pushFrame.Buffer.NumChannels = frame.NumChannels;
                pushFrame.Buffer.SampleRate = frame.SampleRate;
                pushFrame.Buffer.SamplesPerChannel = frame.SamplesPerChannel;

                using var response = request.Send();

                pushFrame.Buffer.DataPtr = 0;
                pushFrame.Buffer.NumChannels = 0;
                pushFrame.Buffer.SampleRate = 0;
                pushFrame.Buffer.SamplesPerChannel = 0;
            }
            catch (Exception e)
            {
                Utils.Error("Audio Framedata error: " + e.Message + "\nStackTrace: " + e.StackTrace);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                Utils.Error($"{nameof(AudioClipRtcAudioSource)} is already disposed");
                return;
            }

            disposed = true;

            using (var guard = buffer.Lock()) guard.Value.Dispose();

            audioResampler.Dispose();
            UnityEngine.Object.Destroy(audioFilter.gameObject);

            if (handleBorrowed == false)
                handle.Dispose();

            audioVolumeLoopCancellationTokenSource.Cancel();
        }
    }
}
