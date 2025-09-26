using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Audio;
using Livekit.Types;
using RichTypes;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class AudioStream : IAudioStream
    {
        private static readonly ResampleQueue Queue = new();

        private readonly IAudioStreams audioStreams;
        private readonly FfiHandle handle;

        private readonly Mutex<NativeAudioBuffer> buffer = new(new NativeAudioBuffer(200));

        private int targetChannels;
        private int targetSampleRate;

        private bool disposed;

        public AudioStream(
            IAudioStreams audioStreams,
            OwnedAudioStream ownedAudioStream
        )
        {
            this.audioStreams = audioStreams;
            handle = IFfiHandleFactory.Default.NewFfiHandle(ownedAudioStream.Handle!.Id);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;
            Queue.Register(this);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            handle.Dispose();
            using (var guard = buffer.Lock()) guard.Value.Dispose();

            FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
            audioStreams.Release(this);
            Queue.UnRegister(this);
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


            Queue.Enqueue(this, e.FrameReceived.Frame);

            // TODO
            // SIMD integration
            // MOVE UNITY sampling to buffer already, don't do it on audio thread
        }

        private class ResampleQueue
        {
            private readonly BlockingCollection<(AudioStream author, OwnedAudioFrameBuffer buffer)> bufferQueue = new();
            private readonly AudioResampler audioResampler = AudioResampler.New();
            private readonly HashSet<AudioStream> registeredStreams = new();

            private CancellationTokenSource? cancellationTokenSource;

            public void Register(AudioStream audioStream)
            {
                lock (registeredStreams)
                {
                    registeredStreams.Add(audioStream);
                    if (cancellationTokenSource == null)
                    {
                        StartThread();
                    }
                }
            }

            public void UnRegister(AudioStream audioStream)
            {
                lock (registeredStreams)
                {
                    registeredStreams.Remove(audioStream);
                    if (registeredStreams.Count == 0)
                    {
                        cancellationTokenSource?.Cancel();
                        cancellationTokenSource = null;
                    }
                }
            }

            public void Enqueue(AudioStream stream, OwnedAudioFrameBuffer buffer)
            {
                bufferQueue.Add((stream, buffer));
            }

            private void ProcessCandidate(AudioStream stream, OwnedAudioFrameBuffer buffer)
            {
                // We need to pass the exact 10ms chunks, otherwise - crash
                // Example
                // #                                                                                             
                // # Fatal error in: ../common_audio/resampler/push_sinc_resampler.cc, line 52                   
                // # last system error: 1                                                                        
                // # Check failed: source_length == resampler_->request_frames() (1104 vs. 480)                  
                // #   
                using var rawFrame = new OwnedAudioFrame(buffer);

                if (stream.targetChannels == 0 || stream.targetSampleRate == 0) return;
                using var frame = audioResampler.RemixAndResample(rawFrame, (uint)stream.targetChannels,
                    (uint)stream.targetSampleRate);
                using var guard = stream.buffer.Lock();
                guard.Value.Write(frame.AsPCMSampleSpan(), frame.NumChannels, frame.SampleRate);
            }

            private void StartThread()
            {
                var token = cancellationTokenSource = new CancellationTokenSource();
                new Thread(() =>
                    {
                        try
                        {
                            foreach (var (author, ownedAudioFrameBuffer)
                                     in bufferQueue.GetConsumingEnumerable(token.Token)!)
                                ProcessCandidate(author, ownedAudioFrameBuffer);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected
                        }
                    }
                ).Start();
            }
        }
    }
}