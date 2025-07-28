using System;
using System.Linq;
using System.Runtime.InteropServices;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using RichTypes;
using UnityEngine;

namespace LiveKit.Audio
{
    public class MicrophoneRtcAudioSource : IRtcAudioSource, IDisposable
    {
        private const int DEFAULT_NUM_CHANNELS = 2;
        private readonly AudioBuffer buffer = new();
        private readonly object lockObject = new();

        private readonly AudioSource audioSource;
        private readonly IAudioFilter audioFilter;
        private readonly Apm apm;
        private readonly ApmReverseStream? reverseStream;
        private readonly GameObject gameObject;

        private bool handleBorrowed;
        private bool disposed;

        private readonly FfiHandle handle;

        private MicrophoneRtcAudioSource(
            AudioSource audioSource,
            IAudioFilter audioFilter,
            Apm apm,
            ApmReverseStream? apmReverseStream
        )
        {
            reverseStream = apmReverseStream;

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
            this.audioSource = audioSource;
            this.audioFilter = audioFilter;
            this.apm = apm;
        }

        public static Result<MicrophoneRtcAudioSource> New(string? microphoneName = null)
        {
            Apm apm = Apm.NewDefault();
            apm.SetStreamDelay(Apm.EstimateStreamDelayMs());

            Result<ApmReverseStream> reverseStream = ApmReverseStream.New(apm);
            if (reverseStream.Success == false)
            {
                return Result<MicrophoneRtcAudioSource>.ErrorResult(
                    $"Cannot create reverse stream: {reverseStream.ErrorMessage}"
                );
            }

            GameObject microphoneObject = new GameObject("microphone");

            microphoneName ??= Microphone.devices!.First();

            var audioSource = microphoneObject.AddComponent<AudioSource>();
            audioSource.loop = true;
            audioSource.clip = Microphone.Start(microphoneName, true, 1, 48000); //frequency is not guaranteed by Unity

            var audioFilter = microphoneObject.AddComponent<AudioFilter>();
            // Prevent microphone feedback
            microphoneObject.AddComponent<OmitAudioFilter>();

            return Result<MicrophoneRtcAudioSource>.SuccessResult(
                new MicrophoneRtcAudioSource(audioSource, audioFilter, apm, reverseStream.Value)
            );
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

        public void Start()
        {
            Stop();
            if (!audioFilter?.IsValid == true || !audioSource)
            {
                Utils.Error("AudioFilter or AudioSource is null - cannot start audio capture");
                return;
            }

            audioFilter.AudioRead += OnAudioRead;
            audioSource.Play();
            reverseStream?.Start();
        }

        public void Stop()
        {
            if (audioFilter?.IsValid == true) audioFilter.AudioRead -= OnAudioRead;
            if (audioSource) audioSource.Stop();
            reverseStream?.Stop();

            lock (lockObject)
            {
                buffer.Dispose();
            }
        }

        private void OnAudioRead(Span<float> data, int channels, int sampleRate)
        {
            lock (lockObject)
            {
                buffer.Write(data, (uint)channels, (uint)sampleRate);
                while (true)
                {
                    var frameResult = buffer.ReadDuration(ApmFrame.FRAME_DURATION_MS);
                    if (frameResult.Has == false) break;
                    using var frame = frameResult.Value;

                    var audioBytes = MemoryMarshal.Cast<byte, PCMSample>(frame.AsSpan());

                    var apmFrame = ApmFrame.New(
                        audioBytes,
                        frame.NumChannels,
                        frame.SamplesPerChannel,
                        new SampleRate(frame.SampleRate),
                        out string? error
                    );
                    if (error != null)
                    {
                        Utils.Error($"Error during creation ApmFrame: {error}");
                        break;
                    }

                    var apmResult = apm.ProcessStream(apmFrame);
                    if (apmResult.Success == false)
                        Utils.Error($"Error during processing stream: {apmResult.ErrorMessage}");

                    ProcessAudioFrame(frame);
                }
            }
        }

        private void ProcessAudioFrame(in AudioFrame frame)
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
                Utils.Error($"{nameof(MicrophoneRtcAudioSource)} is already disposed");
                return;
            }

            disposed = true;

            buffer.Dispose();
            apm.Dispose();
            reverseStream?.Dispose();

            if (handleBorrowed == false)
                handle.Dispose();

            if (gameObject)
                UnityEngine.Object.Destroy(gameObject);
        }
    }
}