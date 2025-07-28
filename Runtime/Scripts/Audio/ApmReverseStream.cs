using System;
using System.Runtime.InteropServices;
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
        private readonly AudioFilter audioFilter;

        internal ApmReverseStream(AudioFilter audioFilter, Apm apm)
        {
            this.audioFilter = audioFilter;
            this.apm = apm;
        }

        public static Result<ApmReverseStream> New(Apm apm)
        {
            var audioListener = GameObject.FindObjectOfType<AudioListener>();
            if (audioListener == null)
            {
                return Result<ApmReverseStream>.ErrorResult("AudioListener not found in scene");
            }

            var audioFilter = audioListener.gameObject.AddComponent<AudioFilter>();
            if (audioFilter == null)
            {
                return Result<ApmReverseStream>.ErrorResult("Cannot add audioFilter");
            }

            return Result<ApmReverseStream>.SuccessResult(new ApmReverseStream(audioFilter, apm));
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
            captureBuffer.Write(data, (uint)channels, (uint)sampleRate);
            while (true)
            {
                var frameResult = captureBuffer.ReadDuration(ApmFrame.FRAME_DURATION_MS);
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