#if !UNITY_WEBGL

using System;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit.Rooms.VideoStreaming
{
    public readonly struct VideoLastFrame : IDisposable
    {
        private readonly VideoBufferInfo info;
        private readonly FfiHandle ffiHandle;

        public VideoLastFrame(VideoBufferInfo info, FfiHandle handle)
        {
            this.info = info;
            ffiHandle = handle;
        }

        public uint Width => info.Width;
        public uint Height => info.Height;

        public IntPtr Data => (IntPtr)info.DataPtr;

        public int MemorySize => (int)(info.Height * info.Stride);

        public void Dispose()
        {
            ffiHandle.Dispose();
        }
    }
}

#endif
