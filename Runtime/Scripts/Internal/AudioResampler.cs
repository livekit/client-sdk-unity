using System;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    public class AudioResampler
    {
        internal readonly OwnedAudioResampler resampler;

        public AudioResampler()
        {
            var newResampler = new NewAudioResamplerRequest();
            var request = new FfiRequest();
            request.NewAudioResampler = newResampler;

            var resp = FfiClient.SendRequest(request);
            resampler = resp.NewAudioResampler.Resampler;
        }

        public AudioFrame RemixAndResample(AudioFrame frame, uint numChannels, uint sampleRate) {
            var remix = new RemixAndResampleRequest();
            remix.ResamplerHandle = resampler.Handle.Id;
            remix.Buffer = frame.Info;
            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;

            var request = new FfiRequest();
            request.RemixAndResample = remix;

            var res = FfiClient.SendRequest(request);
            if(res.RemixAndResample == null) {
                return null;
            }
            var newBuffer = res.RemixAndResample.Buffer;
            return new AudioFrame(newBuffer.Handle, newBuffer.Info);
        }
    }
}
