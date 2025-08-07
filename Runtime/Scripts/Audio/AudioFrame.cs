using System;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;
using UnityEngine.Assertions;

namespace LiveKit.Audio
{
    public interface IAudioFrame
    {
        public uint NumChannels { get; }
        public uint SampleRate { get; }
        public uint SamplesPerChannel { get; }
        public IntPtr Data { get; }

        public bool Disposed { get; }
    }


    public static class AudioFrameExtensions
    {
        public static Span<byte> AsSpan<TAudioFrame>(this TAudioFrame frame) where TAudioFrame : IAudioFrame
        {
            if (frame.Disposed)
            {
                Utils.Error("Attempted to access disposed AudioFrame");
                return Span<byte>.Empty;
            }

            unsafe
            {
                return new Span<byte>(frame.Data.ToPointer(), frame.LengthBytes());
            }
        }

        public static Span<PCMSample> AsPCMSampleSpan<TAudioFrame>(this TAudioFrame frame) where TAudioFrame : IAudioFrame
        {
            return MemoryMarshal.Cast<byte, PCMSample>(frame.AsSpan());
        }

        public static int LengthBytes<TAudioFrame>(this TAudioFrame frame) where TAudioFrame : IAudioFrame
        {
            return (int)(frame.SamplesPerChannel * frame.NumChannels * sizeof(short));
        }

        public static uint DurationMs<TAudioFrame>(this TAudioFrame frame) where TAudioFrame : IAudioFrame
        {
            if (frame.SampleRate == 0) return 0;
            return (frame.SamplesPerChannel * 1000) / frame.SampleRate;
        }
    }


    public struct AudioFrame : IAudioFrame, IDisposable
    {
        public uint NumChannels { get; }
        public uint SampleRate { get; }
        public uint SamplesPerChannel { get; }

        private readonly IntPtr _dataPtr;
        private bool _disposed;

        public IntPtr Data => _dataPtr;
        public bool IsValid => _dataPtr != IntPtr.Zero && !_disposed;
        public bool Disposed => _disposed;

        public AudioFrame(uint sampleRate, uint numChannels, uint samplesPerChannel)
        {
            Assert.AreNotEqual(0, sampleRate);
            Assert.AreNotEqual(0, numChannels);

            SampleRate = sampleRate;
            NumChannels = numChannels;
            SamplesPerChannel = samplesPerChannel;
            _disposed = false;

            unsafe
            {
                uint size = samplesPerChannel * numChannels * sizeof(short);
                _dataPtr = new IntPtr(UnsafeUtility.Malloc(size, sizeof(short), Allocator.Persistent)!);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_dataPtr != IntPtr.Zero)
                unsafe
                {
                    UnsafeUtility.Free(_dataPtr.ToPointer(), Allocator.Persistent);
                }

            _disposed = true;
        }
    }
}