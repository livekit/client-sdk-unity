#if !UNITY_WEBGL

using System;
using LiveKit.Audio;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit.Rooms.Streaming.Audio
{
    public struct OwnedAudioFrame : IAudioFrame, IDisposable
    {
        private readonly AudioFrameBufferInfo info;
        private LightFfiHandle handle;

        public uint NumChannels { get; }
        public uint SampleRate { get; }
        public uint SamplesPerChannel { get; }
        public IntPtr Data { get; }
        public bool Disposed { get; private set; }

        public OwnedAudioFrame(OwnedAudioFrameBuffer ownedAudioFrameBuffer)
        {
            handle = new LightFfiHandle(ownedAudioFrameBuffer.Handle.Id);
            info = ownedAudioFrameBuffer.Info;
            SampleRate = info.SampleRate;
            NumChannels = info.NumChannels;
            SamplesPerChannel = info.SamplesPerChannel;
            Data = (IntPtr) info.DataPtr;
            Disposed = false;
        }

        public void Dispose()
        {
            handle.Dispose();
            Disposed = true;
        }
    }
}

#endif
