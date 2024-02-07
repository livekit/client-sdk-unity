using System;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    public class AudioFrame : IDisposable
    {
        private AudioFrameBufferInfo _info;
        private bool _disposed = false;

        internal FfiHandle Handle { get; }

        public uint NumChannels => _info.NumChannels;
        public uint SampleRate => _info.SampleRate;
        public uint SamplesPerChannel => _info.SamplesPerChannel;

        public IntPtr Data => (IntPtr)_info.DataPtr;
        public int Length => (int)(SamplesPerChannel * NumChannels * sizeof(short));

        internal AudioFrame(FfiHandle handle, AudioFrameBufferInfo info)
        {
            Handle = handle;
            _info = info;
        }

        internal AudioFrame(int sampleRate, int numChannels, int samplesPerChannel)
        {
            using var request = FFIBridge.Instance.NewRequest<AllocAudioBufferRequest>();
            var alloc = request.request;
            alloc.SampleRate = (uint)sampleRate;
            alloc.NumChannels = (uint)numChannels;
            alloc.SamplesPerChannel = (uint)samplesPerChannel;
            using var response = request.Send();
            FfiResponse res = response;
            var bufferInfo = res.AllocAudioBuffer.Buffer.Info;
            Handle = IFfiHandleFactory.Default.NewFfiHandle(res.AllocAudioBuffer.Buffer.Handle.Id);
            _info = bufferInfo;

            res.AllocAudioBuffer = null;
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
                IFfiHandleFactory.Default.Release(Handle);
                _disposed = true;
            }
        }
    }
}