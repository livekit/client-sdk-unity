using System;
using LiveKit.Proto;
using LiveKit.Internal;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LiveKit
{
    public class AudioFrame : IDisposable
    {
        private AudioFrameBufferInfo _info;

        private FfiHandle _handle;

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

        internal AudioFrame(OwnedAudioFrameBuffer info)
        {
            _handle = FfiHandle.FromOwnedHandle(info.Handle);
            _info = info.Info;
            _sampleRate = _info.SampleRate;
            _numChannels = _info.NumChannels;
            _samplesPerChannel = _info.SamplesPerChannel;
            _dataPtr = (IntPtr)_info.DataPtr;
        }

        internal AudioFrame(uint sampleRate, uint numChannels, uint samplesPerChannel) {
            _sampleRate = sampleRate;
            _numChannels = numChannels;
            _samplesPerChannel = samplesPerChannel;
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