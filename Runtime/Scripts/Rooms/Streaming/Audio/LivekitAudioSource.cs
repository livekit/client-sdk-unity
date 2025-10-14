using System;
using LiveKit.Audio;
using LiveKit.Internal;
using RichTypes;
using UnityEngine;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class LivekitAudioSource : MonoBehaviour
    {
        private static ulong counter;

        private int sampleRate;
        private Weak<AudioStream> stream = Weak<AudioStream>.Null;
        private AudioSource audioSource = null!;

        private WavWriter? wavWriter;
        private PCMSample[] wavBuffer = Array.Empty<PCMSample>();

        public bool IsWavActive => wavWriter.HasValue;

        public static LivekitAudioSource New(bool explicitName = false)
        {
            var gm = new GameObject();
            var audioSource = gm.AddComponent<AudioSource>();
            var source = gm.AddComponent<LivekitAudioSource>();
            source.audioSource = audioSource;
            if (explicitName) source.name = $"{nameof(LivekitAudioSource)}_{counter++}";
            return source;
        }

        public void Construct(Weak<AudioStream> audioStream)
        {
            stream = audioStream;
        }

        public void Free()
        {
            stream = Weak<AudioStream>.Null;
            DisposeWavWriter();
        }

        private void DisposeWavWriter()
        {
            if (wavWriter.HasValue && wavWriter.Value.IsDisposed() == false)
            {
                wavWriter.Value.Dispose();
                wavWriter = null;
            }
        }

        private void OnDestroy()
        {
            DisposeWavWriter();
        }

        public void Play()
        {
            audioSource.Play();
        }

        public void Stop()
        {
            audioSource.Stop();
        }

        public void SetVolume(float target)
        {
            audioSource.volume = target;
        }

        public Result ToggleRecordWavOutput()
        {
            if (wavWriter.HasValue)
            {
                DisposeWavWriter();
                return Result.SuccessResult();
            }

            return StartRecordWavOutput();
        }

        public Result StartRecordWavOutput()
        {
            if (wavWriter != null)
            {
                return Result.ErrorResult("Already recording");
            }

            string path = StreamKeyUtils.NewPersistentFilePathByName($"livekit_audio_source_hz{sampleRate}");
            Result<WavWriter> writerResult = WavWriter.NewFromPath(path);
            if (writerResult.Success == false)
            {
                return writerResult;
            }

            wavWriter = writerResult.Value;
            return Result.SuccessResult();
        }

        private void OnEnable()
        {
            OnAudioConfigurationChanged(false);
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            sampleRate = AudioSettings.outputSampleRate;

            // Enable recording with different sample_rate
            if (wavWriter.HasValue)
            {
                DisposeWavWriter();
                Result result = StartRecordWavOutput();
                if (result.Success == false)
                {
                    Utils.Error($"Cannot restart wav recording for output: {result.ErrorMessage}");
                }
            }
        }

        // Called by Unity on the Audio thread
        private void OnAudioFilterRead(float[] data, int channels)
        {
            Option<AudioStream> resource = stream.Resource;
            if (resource.Has)
            {
                resource.Value.ReadAudio(data.AsSpan(), channels, sampleRate);
                if (wavWriter.HasValue)
                {
                    if (data.Length != wavBuffer.Length)
                    {
                        wavBuffer = new PCMSample[data.Length];
                    }

                    // TODO SIMD
                    for (var i = 0; i < data.Length; i++)
                    {
                        wavBuffer[i] = PCMSample.FromUnitySample(data[i]);
                    }

                    WavWriter writer = wavWriter.Value;
                    writer.Write(wavBuffer, (uint)channels, (uint)sampleRate);
                    wavWriter = writer;
                }
            }
        }
    }
}