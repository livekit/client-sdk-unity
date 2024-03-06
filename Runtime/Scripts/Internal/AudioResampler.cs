using System;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using UnityEngine;

namespace LiveKit
{
    public class AudioResampler
    {
        internal FfiHandle Handle { get; }

        public AudioResampler()
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioResamplerRequest>();
            using var response = request.Send();
            FfiResponse res = response;
            Handle = IFfiHandleFactory.Default.NewFfiHandle(res.NewAudioResampler!.Resampler!.Handle!.Id);
        }

        public AudioFrame RemixAndResample(AudioFrame frame, uint numChannels, uint sampleRate)
        {
            using var request = FFIBridge.Instance.NewRequest<RemixAndResampleRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();
            var remix = request.request;
            remix.ResamplerHandle = (ulong)Handle.DangerousGetHandle();

            remix.Buffer = audioFrameBufferInfo;
            remix.Buffer.DataPtr = (ulong)frame.Data;
            remix.Buffer.NumChannels = frame.NumChannels;
            remix.Buffer.SampleRate = frame.SampleRate;
            remix.Buffer.SamplesPerChannel = frame.SamplesPerChannel;



            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;
            using var response = request.Send();
            FfiResponse res = response;
            var bufferInfo = res.RemixAndResample!.Buffer; 
            return new AudioFrame(bufferInfo.Info);
        }
    }
}