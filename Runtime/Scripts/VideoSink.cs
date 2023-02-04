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
        public bool IsValid => !_handle.IsClosed && !_handle.IsInvalid;

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

        public I420Buffer ToI420()
        {
            if (!IsValid)
                return null;

            // ToI420Request will free the input buffer, don't drop twice
            // This class instance is now invalid, the users should not use it 
            // after using this function.
            _handle.SetHandleAsInvalid();

            var handleId = new FFIHandleId();
            handleId.Id = (ulong)_handle.DangerousGetHandle();

            var toi420 = new ToI420Request();
            toi420.Buffer = handleId;

            var request = new FFIRequest();
            request.ToI420 = toi420;

            var resp = FFIClient.Instance.SendRequest(request);
            var id = resp.ToI420.NewBuffer.Id;

            return new I420Buffer(new FFIHandle((IntPtr)id), _info);
        }

        public void ToARGB(VideoFormatType format, IntPtr dst, int dstStride, int width, int height)
        {
            if (!IsValid)
                return;

            var handleId = new FFIHandleId();
            handleId.Id = (ulong)_handle.DangerousGetHandle();

            var argb = new ToARGBRequest();
            argb.Buffer = handleId;
            argb.DstPtr = (ulong)dst;
            argb.DstFormat = format;
            argb.DstStride = dstStride;
            argb.DstWidth = width;
            argb.DstHeight = height;

            var request = new FFIRequest();
            request.ToArgb = argb;

            FFIClient.Instance.SendRequest(request);
            GC.KeepAlive(_handle);
        }
    }

    public abstract class PlanarYuvBuffer : VideoFrameBuffer
    {
        private PlanarYuvBufferInfo _info;

        public int ChromaWidth => _info.ChromaWidth;
        public int ChromaHeight => _info.ChromaHeight;
        public int StrideY => _info.StrideY;
        public int StrideU => _info.StrideU;
        public int StrideV => _info.StrideV;
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
        public int StrideY => _info.StrideY;
        public int StrideUV => _info.StrideUv;
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
