using System;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Streaming.Audio;

namespace LiveKit.Internal
{
    public readonly struct AudioResampler : IDisposable
    {
        private readonly FfiHandle handle;

        private AudioResampler(FfiHandle handle)
        {
            this.handle = handle;
        }

        public static AudioResampler New()
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioResamplerRequest>();
            using var response = request.Send();
            FfiResponse res = response;
            var handle = IFfiHandleFactory.Default.NewFfiHandle(res.NewAudioResampler!.Resampler!.Handle!.Id);
            return new AudioResampler(handle);
        }

        public void Dispose()
        {
            handle.Dispose();
        }

        public OwnedAudioFrame RemixAndResample(OwnedAudioFrame frame, uint numChannels, uint sampleRate)
        {
            using var request = FFIBridge.Instance.NewRequest<RemixAndResampleRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();
            var remix = request.request;
            remix.ResamplerHandle = (ulong)handle.DangerousGetHandle();

            remix.Buffer = audioFrameBufferInfo;
            remix.Buffer.DataPtr = (ulong)frame.dataPtr;
            remix.Buffer.NumChannels = frame.numChannels;
            remix.Buffer.SampleRate = frame.sampleRate;
            remix.Buffer.SamplesPerChannel = frame.samplesPerChannel;

            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;
            using var response = request.Send();
            FfiResponse res = response;
            var bufferInfo = res.RemixAndResample!.Buffer;
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