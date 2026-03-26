using System;
using System.Runtime.InteropServices;
using LiveKit.Internal;
using LiveKit.Rooms.Streaming.Audio;
using RichTypes;
using UnityEngine;

namespace LiveKit.Audio
{
    /// <summary>
    /// Captures and processes the reverse audio stream using an <see cref="Apm"/>.
    /// </summary>
    /// <remarks>
    /// The reverse stream is captured from the scene's audio listener.
    /// </remarks>
    internal class ApmReverseStream : IDisposable
    {
        // We don't need a mutex here because the buffer is used only by audio thread
        private readonly AudioBuffer captureBuffer = new();
        private readonly Apm apm; // APM is thread safe
        private readonly GlobalListenerAudioFilter audioFilter;
        private readonly AudioResampler resampler = AudioResampler.New();

        internal ApmReverseStream(GlobalListenerAudioFilter audioFilter, Apm apm)
        {
            this.audioFilter = audioFilter;
            this.apm = apm;
        }

        public static Result<ApmReverseStream> New(Apm apm)
        {
            Result<GlobalListenerAudioFilter> result = GlobalListenerAudioFilter.NewOrExisting();
            if (result.Success == false)
                return Result<ApmReverseStream>.ErrorResult($"Cannot create APM: {result.ErrorMessage}");

            return Result<ApmReverseStream>.SuccessResult(new ApmReverseStream(result.Value, apm));
        }

        public static ApmReverseStream? NewOrNull(Apm apm)
        {
            var reverseStreamResult = New(apm);
            if (reverseStreamResult.Success)
            {
                return reverseStreamResult.Value;
            }

            Debug.LogError($"Cannot create reverse stream: {reverseStreamResult.ErrorMessage}");
            return null;
        }

        internal void Start()
        {
            audioFilter.AudioRead += OnAudioRead;
        }

        internal void Stop()
        {
            audioFilter.AudioRead -= OnAudioRead;
        }

        private void OnAudioRead(Span<float> data, int channels, int sampleRate)
        {
            captureBuffer.Write(data, (uint) channels, (uint) sampleRate);
            while (true)
            {
                var frameResult = captureBuffer.ReadDuration(ApmFrame.FRAME_DURATION_MS);
                if (frameResult.Has == false) break;
                using AudioFrame rawFrame = frameResult.Value;
                using OwnedAudioFrame frame = resampler.LiveKitCompatibleRemixAndResample(rawFrame);

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
                    Debug.LogError($"Error during creation ApmFrame: {error}");
                    break;
                }

                var result = apm.ProcessReverseStream(apmFrame);
                if (result.Success == false)
                    Debug.LogError($"Error during processing reverse frame: {result.ErrorMessage}");
            }
        }

        public void Dispose()
        {
            captureBuffer.Dispose();
            if (audioFilter)
                UnityEngine.Object.Destroy(audioFilter);
            // Doesn't dispose the APM because doesn't own it
        }
    }
}