using System;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    public class AudioResampler
    {
        //internal readonly FfiHandle Handle;
        private FfiHandle _handle;
        internal FfiHandle Handle
        {
            get { return _handle; }
        }

        private CancellationToken _canceltoken;

        public AudioResampler(CancellationToken canceltoken)
        {
            var newResampler = new NewAudioResamplerRequest();
            var request = new FfiRequest();
            request.NewAudioResampler = newResampler;
            _canceltoken = canceltoken;
            var res = FfiClient.SendRequest(request);
            _handle = new FfiHandle((IntPtr)res.NewAudioResampler.Resampler.Handle.Id);
        }

        public AudioFrame RemixAndResample(AudioFrame frame, uint numChannels, uint sampleRate) {

            if (_canceltoken.IsCancellationRequested) return null;
            var remix = new RemixAndResampleRequest();
            remix.ResamplerHandle = (ulong) Handle.DangerousGetHandle();
            remix.Buffer = new AudioFrameBufferInfo() { DataPtr = (ulong) frame.Handle.DangerousGetHandle()};
            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;

            var request = new FfiRequest();
            request.RemixAndResample = remix;

            var res = FfiClient.SendRequest(request);
            // Check if the task has been cancelled

            if (_canceltoken.IsCancellationRequested) return null;
            var bufferInfo = res.RemixAndResample.Buffer;
            var handle = new FfiHandle((IntPtr)bufferInfo.Handle.Id);
            return new AudioFrame(handle, remix.Buffer);
        }
    }
}
