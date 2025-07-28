using System;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;

namespace LiveKit.Audio
{
    public struct AudioFrame : IDisposable
    {
        public readonly uint NumChannels;
        public readonly uint SampleRate;
        public readonly uint SamplesPerChannel;

        private readonly NativeArray<byte> _data;
        private readonly IntPtr _dataPtr;
        private bool _disposed;
        
        public IntPtr Data => _dataPtr;
        public int Length => (int)(SamplesPerChannel * NumChannels * sizeof(short));
        public bool IsValid => _data.IsCreated && !_disposed;

        internal AudioFrame(uint sampleRate, uint numChannels, uint samplesPerChannel)
        {
            SampleRate = sampleRate;
            NumChannels = numChannels;
            SamplesPerChannel = samplesPerChannel;
            _disposed = false;

            unsafe
            {
                _data = new NativeArray<byte>((int)(samplesPerChannel * numChannels * sizeof(short)), Allocator.Persistent);
                _dataPtr = (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(_data);
            }
        }

        public void Dispose()
        {
            if (!_disposed && _data.IsCreated)
            {
                _data.Dispose();
                _disposed = true;
            }
        }

        public Span<byte> AsSpan()
        {
            if (_disposed)
            {
                Utils.Debug("Attempted to access disposed AudioFrame");
                return Span<byte>.Empty;
            }
            
            unsafe
            {
                return new Span<byte>(_dataPtr.ToPointer(), Length);
            }
        }
    }
}