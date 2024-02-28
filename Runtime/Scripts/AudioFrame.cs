using System;
using LiveKit.Proto;
using LiveKit.Internal;

namespace LiveKit
{
    public class AudioFrame : IDisposable
    {
        private AudioFrameBufferInfo _info;

        private FfiOwnedHandle _handle;

        private bool _disposed = false;

        public uint NumChannels => _info.NumChannels;
        public uint SampleRate => _info.SampleRate;
        public uint SamplesPerChannel => _info.SamplesPerChannel;

        public AudioFrameBufferInfo Info => _info;

        public IntPtr Data => (IntPtr)_info.DataPtr;
        public int Length => (int) (SamplesPerChannel * NumChannels * sizeof(short));

        internal AudioFrame(FfiOwnedHandle handle, AudioFrameBufferInfo info)
        {
            _handle = handle;
            _info = info;

        }

        internal AudioFrame(int sampleRate, int numChannels, int samplesPerChannel) {
            var request = new FfiRequest();
            request.NewAudioResampler = new Proto.NewAudioResamplerRequest();

            var resp = FfiClient.SendRequest(request);

            var resample_request = new FfiRequest();

            resample_request.RemixAndResample = new RemixAndResampleRequest();
            resample_request.RemixAndResample.ResamplerHandle = resp.NewAudioResampler.Resampler.Handle.Id;
            resample_request.RemixAndResample.Buffer = _info;
            resample_request.RemixAndResample.SampleRate = (uint)sampleRate;
            resample_request.RemixAndResample.NumChannels = (uint)numChannels;

            resp = FfiClient.SendRequest(resample_request);

            _handle = resp.RemixAndResample.Buffer.Handle;
             _info = resp.RemixAndResample.Buffer.Info;
        }
        ~AudioFrame()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
