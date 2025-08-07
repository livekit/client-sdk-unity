using System;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Audio;
using Livekit.Types;
using RichTypes;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class AudioStream : IAudioStream
    {
        private readonly IAudioStreams audioStreams;
        private readonly FfiHandle handle;
        private readonly AudioResampler audioResampler = AudioResampler.New();

        private readonly Mutex<NativeAudioBuffer> buffer = new(new NativeAudioBuffer(200));

        private int targetChannels;
        private int targetSampleRate;

        private bool disposed;

        public AudioStream(
            IAudioStreams audioStreams,
            OwnedAudioStream ownedAudioStream,
            IAudioRemixConveyor _ //TODO remove
        )
        {
            this.audioStreams = audioStreams;
            handle = IFfiHandleFactory.Default.NewFfiHandle(ownedAudioStream.Handle!.Id);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            handle.Dispose();
            using (var guard = buffer.Lock()) guard.Value.Dispose();
            audioResampler.Dispose();

            FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
            audioStreams.Release(this);
        }

        public void ReadAudio(Span<float> data, int channels, int sampleRate)
        {
            targetChannels = channels;
            targetSampleRate = sampleRate;

            if (disposed)
                return;

            data.Fill(0);

            int samplesPerChannel = data.Length / channels;

            {
                Option<AudioFrame> frameOption;
                using (var guard = buffer.Lock())
                {
                    frameOption = guard.Value.Read(
                        (uint)sampleRate,
                        (uint)channels,
                        (uint)samplesPerChannel
                    );
                }

                if (frameOption.Has == false)
                {
                    return;
                }

                using AudioFrame frame = frameOption.Value;
                Span<PCMSample> span = frame.AsPCMSampleSpan();

                for (int i = 0; i < span.Length; i++)
                {
                    data[i] = span[i].ToFloat();
                }
            }
        }

        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if (e.StreamHandle != (ulong)handle.DangerousGetHandle())
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            // We need to pass the exact 10ms chunks, otherwise - crash
            // Example
            // #                                                                                             
            // # Fatal error in: ../common_audio/resampler/push_sinc_resampler.cc, line 52                   
            // # last system error: 1                                                                        
            // # Check failed: source_length == resampler_->request_frames() (1104 vs. 480)                  
            // #   

            using var rawFrame = new OwnedAudioFrame(e.FrameReceived!.Frame!);

            if (targetChannels == 0 || targetSampleRate == 0) return;
            using var frame = audioResampler.RemixAndResample(rawFrame, (uint)targetChannels, (uint)targetSampleRate);
            using var guard = buffer.Lock();
            guard.Value.Write(frame.AsPCMSampleSpan(), frame.NumChannels, frame.SampleRate);
        }
    }
}