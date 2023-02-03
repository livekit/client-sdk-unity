using System;
using LiveKit.Proto;
using LiveKit.Internal;

namespace LiveKit
{
    public sealed class VideoSink
    {
        public delegate void FrameReceiveDelegate(VideoFrame frame, VideoFrameBuffer buffer);

        private VideoSinkInfo _info;

        internal VideoSink(VideoSinkInfo info)
        {
            _info = info;
        }
    }

    public sealed class VideoFrame
    {
        private VideoFrameInfo _info;

        public int Width => _info.Width;
        public int Height => _info.Height;
        public uint Size => _info.Size;
        public uint Id => _info.Id;
        public long TimestampUs => _info.TimestampUs;
        public long NtpTimeUs => _info.NtpTimeMs;
        public uint TransportFrameId => _info.TransportFrameId;
        public uint Timestamp => _info.Timestamp;
        public VideoRotation Rotation => _info.Rotation;

        public VideoFrame(VideoFrameInfo info)
        {
            _info = info;
        }
    }

    public abstract class VideoFrameBuffer
    {
        private FFIHandle _handle;
        private VideoFrameBufferInfo _info;

        public int Width => _info.Width;
        public int Height => _info.Height;
        public VideoFrameBufferType Type => _info.BufferType;

        // Explicitly ask for FFIHandle 
        internal VideoFrameBuffer(FFIHandle handle, VideoFrameBufferInfo info)
        {
            _handle = handle;
            _info = info;
        }

        internal static VideoFrameBuffer Create(FFIHandle handle, VideoFrameBufferInfo info)
        {
            switch (info.BufferType)
            {
                case VideoFrameBufferType.Native:
                    return new NativeBuffer(handle, info);
                case VideoFrameBufferType.I420:
                    return new I420Buffer(handle, info);
                case VideoFrameBufferType.I420A:
                    return new I420ABuffer(handle, info);
                case VideoFrameBufferType.I422:
                    return new I422Buffer(handle, info);
                case VideoFrameBufferType.I444:
                    return new I444Buffer(handle, info);
                case VideoFrameBufferType.I010:
                    return new I010Buffer(handle, info);
                case VideoFrameBufferType.Nv12:
                    return new NV12Buffer(handle, info);
            }

            return null;
        }
    }

    public abstract class PlanarYuvBuffer : VideoFrameBuffer
    {
        private PlanarYuvBufferInfo _info;

        public int ChromaWidth => _info.ChromaWidth;
        public int ChromaHeight => _info.ChromaHeight;
        public int StriveY => _info.StrideY;
        public int StriveU => _info.StrideU;
        public int StriveV => _info.StrideV;
        public IntPtr DataY => (IntPtr)_info.DataYPtr;
        public IntPtr DataU => (IntPtr)_info.DataUPtr;
        public IntPtr DataV => (IntPtr)_info.DataVPtr;

        internal PlanarYuvBuffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info)
        {
            _info = info.Yuv;
        }
    }
    public abstract class PlanarYuv8Buffer : PlanarYuvBuffer
    {
        internal PlanarYuv8Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public abstract class PlanarYuv16BBuffer : PlanarYuvBuffer
    {
        internal PlanarYuv16BBuffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public abstract class BiplanarYuvBuffer : VideoFrameBuffer
    {
        private BiplanarYuvBufferInfo _info;

        public int ChromaWidth => _info.ChromaWidth;
        public int ChromaHeight => _info.ChromaHeight;
        public int StriveY => _info.StrideY;
        public int StriveUV => _info.StrideUv;
        public IntPtr DataY => (IntPtr)_info.DataYPtr;
        public IntPtr DataUV => (IntPtr)_info.DataUvPtr;

        internal BiplanarYuvBuffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info)
        {
            _info = info.BiYuv;
        }
    }

    public abstract class BiplanarYuv8Buffer : BiplanarYuvBuffer
    {
        internal BiplanarYuv8Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class NativeBuffer : VideoFrameBuffer
    {
        private NativeBufferInfo _info;

        internal NativeBuffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I420Buffer : PlanarYuv8Buffer
    {
        internal I420Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I420ABuffer : I420Buffer
    {
        internal I420ABuffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I422Buffer : PlanarYuv8Buffer
    {
        internal I422Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I444Buffer : PlanarYuv8Buffer
    {
        internal I444Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I010Buffer : PlanarYuv16BBuffer
    {
        internal I010Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class NV12Buffer : BiplanarYuv8Buffer
    {
        internal NV12Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }
}
