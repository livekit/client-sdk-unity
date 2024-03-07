using System;
using LiveKit.Proto;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LiveKit
{
    public class AudioFrame : IDisposable
    {
        private AudioFrameBufferInfo _info;

        private FfiOwnedHandle _handle;

        private bool _disposed = false;

        private uint _numChannels;
        public uint NumChannels => _numChannels;
        private uint _sampleRate;
        public uint SampleRate => _sampleRate;
        private uint _samplesPerChannel;
        public uint SamplesPerChannel => _samplesPerChannel;

        public AudioFrameBufferInfo Info => _info;

        private IntPtr _dataPtr;
        public IntPtr Data => _dataPtr;

        public int Length => (int) (SamplesPerChannel * NumChannels * sizeof(short));

        internal AudioFrame(FfiOwnedHandle handle, AudioFrameBufferInfo info)
        {
            _handle = handle;
            _info = info;
            _sampleRate = _info.SampleRate;
            _numChannels = _info.NumChannels;
            _samplesPerChannel = _info.SamplesPerChannel;
            _dataPtr = (IntPtr)info.DataPtr;
        }

        internal AudioFrame(int sampleRate, int numChannels, int samplesPerChannel) {
            _sampleRate = (uint)sampleRate;
            _numChannels = (uint)numChannels;
            _samplesPerChannel = (uint)samplesPerChannel;
            unsafe
            {
                var data = new NativeArray<byte>(Length, Allocator.Persistent);
                _dataPtr = (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(data);
            }
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
