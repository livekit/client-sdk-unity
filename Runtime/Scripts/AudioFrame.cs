using System;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;

namespace LiveKit
{
    public class AudioFrame : IDisposable
    {
        private bool _disposed = false;

        private uint _numChannels;
        public uint NumChannels => _numChannels;
        private uint _sampleRate;
        public uint SampleRate => _sampleRate;
        private uint _samplesPerChannel;
        public uint SamplesPerChannel => _samplesPerChannel;

        private IntPtr _dataPtr;
        public IntPtr Data => _dataPtr;
        public int Length => (int)(SamplesPerChannel * NumChannels * sizeof(short));

        internal AudioFrame(AudioFrameBufferInfo info)
        {
            _sampleRate = info.SampleRate;
            _numChannels = info.NumChannels;
            _samplesPerChannel = info.SamplesPerChannel;
            _dataPtr = (IntPtr)info.DataPtr;
        }

        internal AudioFrame(uint sampleRate, uint numChannels, uint samplesPerChannel)
        {
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