using System;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    public class AudioResampler
    {
        internal readonly FfiHandle Handle;

        public AudioResampler()
        {
            var newResampler = new NewAudioResamplerRequest();
            var request = new FFIRequest();
            request.NewAudioResampler = newResampler;

            var res = FfiClient.SendRequest(request);
            Handle = new FfiHandle((IntPtr)res.NewAudioResampler.Handle.Id);
        }

        public AudioFrame RemixAndResample(AudioFrame frame, uint numChannels, uint sampleRate) {
            var remix = new RemixAndResampleRequest();
            remix.ResamplerHandle = new FFIHandleId { Id = (ulong) Handle.DangerousGetHandle()};
            remix.BufferHandle = new FFIHandleId { Id = (ulong) frame.Handle.DangerousGetHandle()};
            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;

            var request = new FFIRequest();
            request.RemixAndResample = remix;

            var res = FfiClient.SendRequest(request);
            var bufferInfo = res.RemixAndResample.Buffer;
            var handle = new FfiHandle((IntPtr)bufferInfo.Handle.Id);
            return new AudioFrame(handle, bufferInfo);
        }
    }
}
