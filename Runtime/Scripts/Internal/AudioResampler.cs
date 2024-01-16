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

        private CancellationToken canceltoken;

        public AudioResampler(CancellationToken canceltoken)
        {
            var newResampler = new NewAudioResamplerRequest();
            var request = new FfiRequest();
            request.NewAudioResampler = newResampler;

            this.canceltoken = canceltoken;

            Init(request);
        }

        async void Init(FfiRequest request)
        {
            var res = await FfiClient.SendRequest(request);
            _handle = new FfiHandle((IntPtr)res.NewAudioResampler.Resampler.Handle.Id);
        }

        async public Task<AudioFrame> RemixAndResample(AudioFrame frame, uint numChannels, uint sampleRate) {
            var remix = new RemixAndResampleRequest();
            remix.ResamplerHandle = (ulong) Handle.DangerousGetHandle();
            remix.Buffer = new AudioFrameBufferInfo() { DataPtr = (ulong) frame.Handle.DangerousGetHandle()};
            remix.NumChannels = numChannels;
            remix.SampleRate = sampleRate;

            var request = new FfiRequest();
            request.RemixAndResample = remix;

            var res = await FfiClient.SendRequest(request);
            // Check if the task has been cancelled
            if (canceltoken.IsCancellationRequested)
            {
                // End the task
                Utils.Debug("Task cancelled");
                return null;
            }
            var bufferInfo = res.RemixAndResample.Buffer;
            var handle = new FfiHandle((IntPtr)bufferInfo.Handle.Id);
            return new AudioFrame(handle, remix.Buffer);
        }
    }
}
