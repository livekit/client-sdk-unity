#if !UNITY_WEBGL

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Audio;
using LiveKit.Internal.FFIClients;
using LiveKit.Rooms.Tracks;
using Livekit.Types;
using RichTypes;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class AudioStream : IDisposable
    {
        private readonly StreamKey streamKey;
        private readonly ulong trackHandle;

        private AudioStreamInternal currentInternal;
        private uint currentSampleRate;
        private uint currentChannels;

        public AudioStreamInfo AudioStreamInfo => currentInternal.audioStreamInfo;

        public WavTeeControl WavTeeControl => currentInternal.WavTeeControl;

        public AudioStream(StreamKey streamKey, ITrack track)
        {
            this.streamKey = streamKey;
            trackHandle = (ulong)track.Handle!.DangerousGetHandle();
            currentSampleRate = (uint)UnityEngine.AudioSettings.outputSampleRate;
            currentChannels = 2;
            currentInternal = NewInternal(streamKey, trackHandle, currentChannels, currentSampleRate);
        }

        private static AudioStreamInternal NewInternal(
            StreamKey streamKey,
            ulong trackHandle,
            uint channels,
            uint sampleRate
        )
        {
            using FfiRequestWrap<NewAudioStreamRequest> request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
            var newStream = request.request;
            newStream.TrackHandle = trackHandle;
            newStream.Type = AudioStreamType.AudioStreamNative;
            newStream.SampleRate = sampleRate;
            newStream.NumChannels = channels;
            AudioStreamInfo audioStreamInfo = new AudioStreamInfo(streamKey, newStream.NumChannels, newStream.SampleRate);

            using FfiResponseWrap response = request.Send();
            FfiResponse res = response;

            OwnedAudioStream streamInfo = res.NewAudioStream!.Stream!;

            return new AudioStreamInternal(streamInfo, audioStreamInfo, channels, sampleRate);
        }

        /// <summary>
        /// Supposed to be called from Unity's audio thread.
        /// </summary>
        public void ReadAudio(Span<float> data, int channels, int sampleRate)
        {
            if (currentChannels != channels || currentSampleRate != sampleRate)
            {
                bool wasWavActive = currentInternal.WavTeeControl.IsWavActive;

                currentInternal.Dispose();
                currentChannels = (uint)channels;
                currentSampleRate = (uint)sampleRate;
                currentInternal = NewInternal(streamKey, trackHandle, currentChannels, currentSampleRate);

                if (wasWavActive)
                {
                    currentInternal.WavTeeControl.StartWavTeeToDisk();
                }
            }

            currentInternal.ReadAudio(data, channels, sampleRate);
        }

        public void Dispose()
        {
            currentInternal.Dispose();
        }
    }

    public class AudioStreamInternal : IDisposable
    {
        private readonly FfiHandle handle;

        /// <summary>
        /// Keep under single lock for the use case, avoid unneeded multiple mutex locking
        /// </summary>
        private readonly Mutex<NativeAudioBufferResampleTee> buffer =
            new(
                new NativeAudioBufferResampleTee(
                    new NativeAudioBuffer(200),
                    default,
                    default
                )
            );

        private bool disposed;

        public readonly AudioStreamInfo audioStreamInfo;
        private readonly uint internalChannels;
        private readonly uint internalSampleRate;

        public WavTeeControl WavTeeControl
        {
            get
            {
                string networkFilePath =
                    StreamKeyUtils.NewPersistentFilePathByStreamKey(audioStreamInfo.streamKey, "network");
                string resampleFilePath =
                    StreamKeyUtils.NewPersistentFilePathByStreamKey(audioStreamInfo.streamKey,
                        "network_duplicate"); // TODO remove later
                return new WavTeeControl(buffer, beforeWavFilePath: networkFilePath, afterWavFilePath: resampleFilePath);
            }
        }

        public AudioStreamInternal(
            OwnedAudioStream ownedAudioStream,
            AudioStreamInfo audioStreamInfo,
            uint channels,
            uint sampleRate
        )
        {
            this.audioStreamInfo = audioStreamInfo;
            internalChannels = channels;
            internalSampleRate = sampleRate;

            handle = IFfiHandleFactory.Default.NewFfiHandle(ownedAudioStream.Handle!.Id);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;
        }

        /// <summary>
        /// Supposed to be disposed ONLY by AudioStreams
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            handle.Dispose();
            using (var guard = buffer.Lock())
            {
                guard.Value.Dispose();
            }

            FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
        }

        /// <summary>
        /// Supposed to be called from Unity's audio thread.
        /// </summary>
        public void ReadAudio(Span<float> data, int channels, int sampleRate)
        {
            if (disposed)
                return;

            if (channels != internalChannels || sampleRate != internalSampleRate)
            {
                Utils.Error(
                    $"Calling ReadAudio on {nameof(AudioStreamInternal)} with wrong args: channels {channels}, sampleRate: {sampleRate}; but intended: channels {internalChannels}, sampleRate {internalSampleRate}"
                );
                return;
            }

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

            using var frame = new OwnedAudioFrame(e.FrameReceived!.Frame!);

            if (frame.NumChannels != internalChannels || frame.SampleRate != internalSampleRate)
            {
                Utils.Error(
                    $"Received frame on {nameof(AudioStreamInternal)} with wrong args from frame: channels {frame.NumChannels}, sampleRate: {frame.SampleRate}; but intended: channels {internalChannels}, sampleRate {internalSampleRate}"
                );
                return;
            }

            using var guard = buffer.Lock();
            guard.Value.Write(frame);
            guard.Value.TryWriteWavTee(frame, frame);

            // TODO
            // SIMD integration
            // MOVE UNITY sampling to buffer already, don't do it on audio thread
        }
    }

    public readonly struct AudioStreamInfo
    {
        public readonly StreamKey streamKey;
        public readonly uint numChannels;
        public readonly uint sampleRate;

        public AudioStreamInfo(StreamKey streamKey, uint numChannels, uint sampleRate)
        {
            this.streamKey = streamKey;
            this.numChannels = numChannels;
            this.sampleRate = sampleRate;
        }
    }
}

#endif
