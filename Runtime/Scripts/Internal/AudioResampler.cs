using System;
using LiveKit.Audio;
using LiveKit.client_sdk_unity.Runtime.Scripts.Internal.FFIClients;
using LiveKit.Internal.FFIClients;
using LiveKit.Internal.FFIClients.Pools;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Streaming.Audio;
using UnityEngine;
using UnityEngine.Assertions;

namespace LiveKit.Internal
{
    public readonly struct AudioResampler : IDisposable
    {
        private readonly FfiHandle handle;
        private readonly FFIBridge ffiBridge;

        private AudioResampler(FfiHandle handle, FFIBridge ffiBridge)
        {
            this.handle = handle;
            this.ffiBridge = ffiBridge;
        }

        public static AudioResampler New()
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioResamplerRequest>();
            using var response = request.Send();
            FfiResponse res = response;
            var handle = IFfiHandleFactory.Default.NewFfiHandle(res.NewAudioResampler!.Resampler!.Handle!.Id);
            
            // Thread local pool for performance, default instance is thread safe and has locks
            FFIBridge bridge = new FFIBridge(FfiClient.Instance, new ThreadLocalMultiPool());
            
            return new AudioResampler(handle, bridge);
        }

        public void Dispose()
        {
            handle.Dispose();
        }

        public OwnedAudioFrame LiveKitCompatibleRemixAndResample<TAudioFrame>(
            TAudioFrame frame,
            uint? overrideNumChannels = null) where TAudioFrame : IAudioFrame
        {
            return RemixAndResample(frame, overrideNumChannels ?? frame.NumChannels, SampleRate.Hz48000.valueHz);
        }

        public OwnedAudioFrame RemixAndResample<TAudioFrame>(
            TAudioFrame frame,
            uint numChannels,
            uint sampleRate
        ) where TAudioFrame : IAudioFrame
        {
            Assert.AreNotEqual(0, sampleRate);
            Assert.AreNotEqual(0, numChannels);

            var duration = frame.DurationMs();
            if (duration != 10) //10 ms required by WebRTC
            {
                Debug.LogError($"Cannot resample, duration is not 10 ms, instead {duration} ms");
                //TODO result
                throw new Exception();
            }

            using FfiRequestWrap<RemixAndResampleRequest> request = ffiBridge.NewRequest<RemixAndResampleRequest>();
            using SmartWrap<AudioFrameBufferInfo> audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();
            RemixAndResampleRequest remix = request.request;
            remix.ResamplerHandle = (ulong)handle.DangerousGetHandle();

            remix.Buffer = audioFrameBufferInfo;
            remix.Buffer.DataPtr = (ulong)frame.Data;
            remix.Buffer.NumChannels = frame.NumChannels;
            remix.Buffer.SampleRate = frame.SampleRate;
            remix.Buffer.SamplesPerChannel = frame.SamplesPerChannel;

            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;
            using FfiResponseWrap response = request.Send();
            FfiResponse res = response;
            OwnedAudioFrameBuffer bufferInfo = res.RemixAndResample!.Buffer;
            return new OwnedAudioFrame(bufferInfo);
        }


        public class ThreadSafe : IDisposable
        {
            private readonly AudioResampler resampler = New();

            /// <summary>
            /// Takes ownership of the frame and is responsible for its disposal
            /// </summary>
            public OwnedAudioFrame RemixAndResample(OwnedAudioFrame frame, uint numChannels, uint sampleRate)
            {
                using (frame)
                {
                    lock (this)
                    {
                        return resampler.RemixAndResample(frame, numChannels, sampleRate);
                    }
                }
            }

            public void Dispose()
            {
                resampler.Dispose();
            }
        }
    }
}