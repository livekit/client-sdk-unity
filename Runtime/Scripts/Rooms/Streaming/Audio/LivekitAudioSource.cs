#if !UNITY_WEBGL || UNITY_EDITOR

using System;
using System.Runtime.CompilerServices;
using LiveKit.Audio;
using LiveKit.Internal;
using RichTypes;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class LivekitAudioSource : MonoBehaviour
    {
        public enum IldProfile : byte
        {
            Subtle   = 0,
            Moderate = 1,
            Strong   = 2,
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AsAlpha(IldProfile profile) =>
            profile switch
            {
                IldProfile.Subtle => 1.2f, // α=1.2, ≈ −8 dB at 90° — average across voice band
                IldProfile.Moderate => 1.5f, // α=1.5, ≈ −10 dB at 90° — slightly emphasized
                _ => 2.0f, // α=2.0, ≈ −13 dB at 90° — high-freq end of natural ILD
            };

        private static readonly ProfilerMarker markerSpatialPanningDSP = new ("LiveKit.Spatial.ILD.Exponential");

        private static ulong counter;

        private int sampleRate;
        private Weak<AudioStream> stream = Weak<AudioStream>.Null;

        private volatile float azimuth;
        private volatile float elevation;
        private float prevGainL = 1.0f;
        private float prevGainR = 1.0f;

        [Header("SPATIALIZATION")]
        [Tooltip("Enable L/R panning based on azimuth/elevation set via SetSpatialAngles.")]
        [SerializeField] private volatile bool spatialize;
        
        [Header("IDL")]
        [Tooltip("Interaural Level Difference (ILD) strength. 0 = no panning, 1 = full silence on far ear at 90°.")]
        [SerializeField, Range(0f, 1f)] private volatile float ildStrength = 0.75f;
        [Tooltip("ILD attenuation curve at 90°: Subtle ≈ −8 dB (natural voice avg), Moderate ≈ −10 dB, Strong ≈ −13 dB (high-freq end).")]
        [SerializeField] private IldProfile ildProfile = IldProfile.Strong;
        
        [Header("CLICK PREVENTION")]
        [Tooltip("Gain ramp duration in milliseconds at buffer start to prevent clicks on rapid azimuth changes. 0 = no ramp (cheapest, may click).")]
        [SerializeField, Range(0f, 30f)] private volatile float panningRampMs = 5f;
        [Tooltip("Min per-buffer gain change that activates the ramp. ~0.05 is an audible click; default 0.01 is a conservative threshold (5x safety margin). Lower = safer but more lerp work.")]
        [SerializeField, Range(0f, 0.1f)] private volatile float gainDeltaThreshold = 0.01f;
        
        private WavWriter? wavWriter;
        private PCMSample[] wavBuffer = Array.Empty<PCMSample>();

        public bool IsWavActive => wavWriter.HasValue;
        public AudioSource AudioSource { get; private set; } = null!;

        public static LivekitAudioSource New(bool explicitName = false, bool isSpatial = false)
        {
            var gm = new GameObject();
            var source = gm.AddComponent<LivekitAudioSource>();
            source.AudioSource = gm.AddComponent<AudioSource>();
            source.spatialize = isSpatial;
            if (explicitName) source.name = $"{nameof(LivekitAudioSource)}_{counter++}";
            return source;
        }
        
        public void SetSpatialAngles(float azimuth, float elevation)
        {
            this.azimuth = azimuth;
            this.elevation = elevation;
        }

        public void SetSpatialSettings(bool spatialize, float ildStrength)
        {
            this.spatialize = spatialize;
            this.ildStrength = ildStrength;
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
            AudioSource.Play();
        }

        public void Stop()
        {
            AudioSource.Stop();
        }

        public void SetVolume(float target)
        {
            AudioSource.volume = target;
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
                    LiveKit.Internal.Utils.Error($"Cannot restart wav recording for output: {result.ErrorMessage}");
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

                // TODO: handle 5.1 and 7.1 sound system cases
                if (spatialize && channels == 2)
                    ApplySpatialPanning(data, channels);

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplySpatialPanning(float[] data, int channels)
        {
            using var _ = markerSpatialPanningDSP.Auto();

            int samplesPerChannel = data.Length / channels;

            float alpha = AsAlpha(ildProfile);


            float pan = math.sin(azimuth) * math.cos(elevation) * ildStrength;
            float gainL = math.exp(-alpha * math.max(0f, pan));
            float gainR = math.exp(-alpha * math.max(0f, -pan));

            float gainDelta = math.max(math.abs(gainL - prevGainL), math.abs(gainR - prevGainR));
            bool rampNeeded = panningRampMs > 0f && gainDelta > gainDeltaThreshold;
            int rampLen = rampNeeded
                ? math.min((int)(panningRampMs * sampleRate * 0.001f), samplesPerChannel)
                : 0;

            // click smoothing for fast moves if rampNeeded
            {
                float invRamp = 1f / rampLen;

                for (int i = 0; i < rampLen; i++)
                {
                    float t = i * invRamp;
                    int offset = i * channels;
                    float mono = data[offset];
                    data[offset]     = mono * math.lerp(prevGainL, gainL, t);
                    data[offset + 1] = mono * math.lerp(prevGainR, gainR, t);
                }
            }

            // basic panning
            {
                for (int i = rampLen; i < samplesPerChannel; i++)
                {
                    int offset = i * channels;
                    float mono = data[offset];
                    data[offset]     = mono * gainL;
                    data[offset + 1] = mono * gainR;
                }
            }

            prevGainL = gainL;
            prevGainR = gainR;
        }
    }
}

#endif
