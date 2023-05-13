using System;
using LiveKit.Proto;
using LiveKit.Internal;

namespace LiveKit
{
    public class AudioFrame : IDisposable
    {
        private AudioFrameBufferInfo _info;

        internal readonly FfiHandle Handle;
        private bool _disposed = false;

        public uint NumChannels => _info.NumChannels;
        public uint SampleRate => _info.SampleRate;
        public uint SamplesPerChannel => _info.SamplesPerChannel;

        public IntPtr Data => (IntPtr)_info.DataPtr;
        public int Length => (int) (SamplesPerChannel * NumChannels * sizeof(short));

        internal AudioFrame(FfiHandle handle, AudioFrameBufferInfo info)
        {
            Handle = handle;
            _info = info;
        }

        internal AudioFrame(int sampleRate, int numChannels, int samplesPerChannel) {
            var alloc = new AllocAudioBufferRequest();
            alloc.SampleRate = (uint) sampleRate;
            alloc.NumChannels = (uint) numChannels;
            alloc.SamplesPerChannel = (uint) samplesPerChannel;

            var request = new FFIRequest();
            request.AllocAudioBuffer = alloc;

            var res = FfiClient.SendRequest(request);
            var bufferInfo = res.AllocAudioBuffer.Buffer;

            Handle = new FfiHandle((IntPtr)bufferInfo.Handle.Id);
            _info = bufferInfo;
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
                Handle.Dispose();
                _disposed = true;
            }
        }
    }
}
