using System;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;

namespace LiveKit
{
    public sealed class VideoFrame
    {
        private VideoFrameInfo _info;

        public long Timestamp => _info.TimestampUs;
        public VideoRotation Rotation => _info.Rotation;

        public VideoFrame(VideoFrameInfo info)
        {
            _info = info;
        }
    }

    public abstract class VideoFrameBuffer : IDisposable
    {
        protected VideoFrameBufferInfo Info;

        internal readonly FfiHandle Handle;
        private bool _disposed = false;

        public uint Width => Info.Width;
        public uint Height => Info.Height;
        public VideoFrameBufferType Type => Info.BufferType;
        public bool IsValid => !Handle.IsClosed && !Handle.IsInvalid;

        // Explicitly ask for FFIHandle 
        protected VideoFrameBuffer(FfiHandle handle, VideoFrameBufferInfo info)
        {
            Handle = handle;
            Info = info;

            var memSize = GetMemorySize();
            if (memSize > 0)
                GC.AddMemoryPressure(memSize);
        }

        ~VideoFrameBuffer()
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
                Handle.Dispose();

                var memSize = GetMemorySize();
                if (memSize > 0)
                    GC.RemoveMemoryPressure(memSize);

                _disposed = true;
            }
        }

        /// Used for GC.AddMemoryPressure(Int64)
        /// TODO(theomonnom): Remove the default implementation when each buffer type is implemented  cc MindTrust_VID
        internal virtual long GetMemorySize()
        {
            return -1;
        }

        /// VideoFrameBuffer takes ownership of the FFIHandle
        internal static VideoFrameBuffer Create(FfiHandle handle, VideoFrameBufferInfo info)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public I420Buffer ToI420()
        {
            if (!IsValid)
                throw new InvalidOperationException("the handle is invalid");

            // ToI420Request will free the input buffer, don't drop twice
            // This class instance is now invalid, the users should not use it 
            // after using this function.
            Handle.SetHandleAsInvalid();

            //var handleId = new VideoFrameBufferInfo();
            //handleId.Id = (ulong)Handle.DangerousGetHandle();

            var toi420 = new ToI420Request();
            //toi420.Buffer = handleId;
            toi420.Handle = (ulong)Handle.DangerousGetHandle();

            var request = new FfiRequest();
            request.ToI420 = toi420;

            var resp = FfiClient.SendRequest(request);
            var newInfo = resp.ToI420.Buffer;
            if (newInfo == null)
                throw new InvalidOperationException("failed to convert");

            var newHandle = new FfiHandle((IntPtr)newInfo.Handle.Id);
            return new I420Buffer(newHandle, newInfo.Info); // TODO MindTrust_VID
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToARGB(VideoFormatType format, IntPtr dst, uint dstStride, uint width, uint height)
        {
            if (!IsValid)
                throw new InvalidOperationException("the handle is invalid");

            var argb = new ToArgbRequest();
            //argb.Buffer = handleId;
            argb.DstPtr = (ulong)dst;
            argb.DstFormat = format;
            argb.DstStride = dstStride;
            argb.DstWidth = width;
            argb.DstHeight = height;

            var request = new FfiRequest();
            request.ToArgb = argb;

            FfiClient.SendRequest(request);
        }
    }

    public abstract class PlanarYuvBuffer : VideoFrameBuffer
    {
        public uint ChromaWidth => Info.Yuv.ChromaWidth;
        public uint ChromaHeight => Info.Yuv.ChromaHeight;
        public uint StrideY => Info.Yuv.StrideY;
        public uint StrideU => Info.Yuv.StrideU;
        public uint StrideV => Info.Yuv.StrideV;
        public IntPtr DataY => (IntPtr)Info.Yuv.DataYPtr;
        public IntPtr DataU => (IntPtr)Info.Yuv.DataUPtr;
        public IntPtr DataV => (IntPtr)Info.Yuv.DataVPtr;

        internal PlanarYuvBuffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }
    public abstract class PlanarYuv8Buffer : PlanarYuvBuffer
    {
        internal PlanarYuv8Buffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public abstract class PlanarYuv16BBuffer : PlanarYuvBuffer
    {
        internal PlanarYuv16BBuffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public abstract class BiplanarYuvBuffer : VideoFrameBuffer
    {
        public uint ChromaWidth => Info.BiYuv.ChromaWidth;
        public uint ChromaHeight => Info.BiYuv.ChromaHeight;
        public uint StrideY => Info.BiYuv.StrideY;
        public uint StrideUV => Info.BiYuv.StrideUv;
        public IntPtr DataY => (IntPtr)Info.BiYuv.DataYPtr;
        public IntPtr DataUV => (IntPtr)Info.BiYuv.DataUvPtr;

        internal BiplanarYuvBuffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public abstract class BiplanarYuv8Buffer : BiplanarYuvBuffer
    {
        internal BiplanarYuv8Buffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class NativeBuffer : VideoFrameBuffer
    {
        internal override long GetMemorySize()
        {
            return 0;
        }

        internal NativeBuffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I420Buffer : PlanarYuv8Buffer
    {
        internal I420Buffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }

        internal override long GetMemorySize()
        {
            var chromaHeight = (Height + 1) / 2;
            return StrideY * Height
                + StrideU * chromaHeight
                + StrideV * chromaHeight;
        }
    }

    public class I420ABuffer : I420Buffer
    {
        internal I420ABuffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I422Buffer : PlanarYuv8Buffer
    {
        internal I422Buffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I444Buffer : PlanarYuv8Buffer
    {
        internal I444Buffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I010Buffer : PlanarYuv16BBuffer
    {
        internal I010Buffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class NV12Buffer : BiplanarYuv8Buffer
    {
        internal NV12Buffer(FfiHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

}
