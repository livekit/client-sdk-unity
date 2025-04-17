using System;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit.Rooms.Streaming.Audio
{
    public readonly struct OwnedAudioFrame : IDisposable
    {
        private readonly AudioFrameBufferInfo info;
        private readonly FfiHandle handle;

        public readonly uint numChannels;
        public readonly uint sampleRate;
        public readonly uint samplesPerChannel;
        public readonly IntPtr dataPtr;

        public int Length => (int)(samplesPerChannel * numChannels * sizeof(short));

        public OwnedAudioFrame(OwnedAudioFrameBuffer ownedAudioFrameBuffer)
        {
            handle = IFfiHandleFactory.Default.NewFfiHandle(ownedAudioFrameBuffer.Handle.Id);
            info = ownedAudioFrameBuffer.Info;
            sampleRate = info.SampleRate;
            numChannels = info.NumChannels;
            samplesPerChannel = info.SamplesPerChannel;
            dataPtr = (IntPtr)info.DataPtr;
        }

        public void Dispose()
        {
            handle.Dispose();
        }

        public Span<byte> AsSpan()
        {
            unsafe
            {
                return new Span<byte>(dataPtr.ToPointer(), Length);
            }
        }
    }
}