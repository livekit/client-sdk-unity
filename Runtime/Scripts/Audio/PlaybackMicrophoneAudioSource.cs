using System;
using LiveKit.Audio;
using LiveKit.Internal;
using LiveKit.Scripts.Audio;
using Livekit.Types;
using RichTypes;
using UnityEngine;

namespace LiveKit.Audio
{
    public class PlaybackMicrophoneAudioSource : MonoBehaviour, IDisposable
    {
        private readonly Mutex<NativeAudioBuffer> buffer =
            new(new NativeAudioBuffer(bufferDurationMs: 200));
        private readonly Mutex<NativeAudioBuffer> microphoneBuffer = new(new NativeAudioBuffer(30));

        private readonly AudioResampler audioResampler = AudioResampler.New();
        private IAudioFilter? microphoneAudioFilter;

        private uint outputSampleRate;
        private uint targetChannels;

        private bool disposed;

        private void Construct(IAudioFilter newMicrophoneAudioFilter)
        {
            microphoneAudioFilter = newMicrophoneAudioFilter;
            microphoneAudioFilter.AudioRead += MicrophoneAudioFilterOnAudioRead;
            outputSampleRate = (uint)AudioSettings.outputSampleRate;
        }

        public static PlaybackMicrophoneAudioSource New(IAudioFilter microphoneAudioFilter, string name)
        {
            var gm = new GameObject(nameof(PlaybackMicrophoneAudioSource) + " - " + name);
            var source = gm.AddComponent<AudioSource>();
            var playback = gm.AddComponent<PlaybackMicrophoneAudioSource>();
            playback.Construct(microphoneAudioFilter);

            source.Play();
            return playback;
        }

        void MicrophoneAudioFilterOnAudioRead(Span<float> data, int channels, int sampleRate)
        {
            using var guard = buffer.Lock();
            using var frame = new AudioFrame((uint)sampleRate, (uint)channels, (uint)(data.Length / channels));
            Span<PCMSample> span = frame.AsPCMSampleSpan();

            if (span.Length != data.Length)
            {
                Debug.LogError($"Inconsistent data frame: data length - {data.Length}, PCM sample length - {span.Length}");
                return;
            }

            for (int i = 0; i < data.Length; i++)
            {
                span[i] = PCMSample.FromUnitySample(data[i]);
            }

            using var microphoneBufferGuard = microphoneBuffer.Lock();
            microphoneBufferGuard.Value.Write(span, (uint)channels, (uint)sampleRate);

            if (targetChannels == 0 || outputSampleRate == 0)
                return;

            while (true)
            {
                uint sample10MS = (uint)(sampleRate * 10 / 1000);
                Option<AudioFrame> bufferedFrame =
                    microphoneBufferGuard.Value.Read((uint)sampleRate, (uint)channels, sample10MS);
                if (bufferedFrame.Has)
                {
                    using var b = bufferedFrame.Value;
                    using var remix = audioResampler.RemixAndResample(b, targetChannels, outputSampleRate);
                    guard.Value.Write(remix);
                }
                else
                {
                    break;
                }
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            targetChannels = (uint)channels;

            int samplesPerChannel = data.Length / channels;
            using var guard = buffer.Lock();
            var frame = guard.Value.Read(outputSampleRate, (uint)channels, (uint)samplesPerChannel);
            if (frame.Has)
            {
                using var f = frame.Value;
                var span = f.AsPCMSampleSpan();
                for (int i = 0; i < span.Length; i++)
                {
                    data[i] = span[i].ToFloat();
                }
            }
        }

        private void OnDestroy()
        {
            if (disposed)
                return;
            disposed = true;

            if (microphoneAudioFilter != null)
            {
                microphoneAudioFilter.AudioRead -= MicrophoneAudioFilterOnAudioRead;
            }

            audioResampler.Dispose();

            using var guard = buffer.Lock();
            guard.Value.Dispose();

            using var microphoneGuard = microphoneBuffer.Lock();
            microphoneGuard.Value.Dispose();
        }

        public void Dispose()
        {
            Destroy(this);
            Destroy(gameObject);
        }
    }
}