using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using UnityEngine;

namespace LiveKit
{
    public class AudioResampler
    {
        internal readonly OwnedAudioResampler resampler;

        public AudioResampler()
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioResamplerRequest>();
            using var response = request.Send();
            FfiResponse res = response;
            resampler = res.NewAudioResampler.Resampler;
        }

        public AudioFrame RemixAndResample(AudioFrame frame, uint numChannels, uint sampleRate)
        {
            using var request = FFIBridge.Instance.NewRequest<RemixAndResampleRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();
            var remix = request.request;
            remix.ResamplerHandle = resampler.Handle.Id;
            remix.Buffer = frame.Info;
            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;

            using var response = request.Send();
            FfiResponse res = response;
            if (res.RemixAndResample == null)
            {
                return null;
            }
            var newBuffer = res.RemixAndResample.Buffer;
            return new AudioFrame(newBuffer);
        }
    }
}