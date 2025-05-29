using System;
using LiveKit.Proto;
using LiveKit.Internal;
using Unity.Collections;

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

        private NativeArray<byte> _allocatedData; // Only used if the frame's data is allocated by Unity
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
                if (_allocatedData.IsCreated)
                {
                    _allocatedData.Dispose();
                }
                _disposed = true;
            }
        }
    }
}