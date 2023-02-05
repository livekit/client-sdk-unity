using System;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace LiveKit
{
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

    public abstract class VideoFrameBuffer : IDisposable
    {
        protected VideoFrameBufferInfo Info;

        private FFIHandle _handle;
        private bool _disposed = false;

        public int Width => Info.Width;
        public int Height => Info.Height;
        public VideoFrameBufferType Type => Info.BufferType;
        public bool IsValid => !_handle.IsClosed && !_handle.IsInvalid;

        // Explicitly ask for FFIHandle 
        protected VideoFrameBuffer(FFIHandle handle, VideoFrameBufferInfo info)
        {
            _handle = handle;
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
                _handle.Dispose();

                var memSize = GetMemorySize();
                if (memSize > 0)
                    GC.RemoveMemoryPressure(memSize);

                _disposed = true;
            }
        }

        /// Used for GC.AddMemoryPressure(Int64)
        /// TODO(theomonnom): Remove the default implementation when each buffer type is implemented
        internal virtual long GetMemorySize()
        {
            return -1;
        }

        /// VideoFrameBuffer takes owenship of the FFIHandle
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public I420Buffer ToI420()
        {
            if (!IsValid)
                throw new SystemException("the handle is invalid");

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

            var resp = FFIClient.SendRequest(request);
            var newInfo = resp.ToI420.NewBuffer;
            if (newInfo == null)
                throw new SystemException("failed to convert");

            var newHandle = new FFIHandle((IntPtr)newInfo.Handle.Id);

            return new I420Buffer(newHandle, newInfo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToARGB(VideoFormatType format, IntPtr dst, int dstStride, int width, int height)
        {
            if (!IsValid)
                throw new SystemException("the handle is invalid");

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

            FFIClient.SendRequest(request);
        }
    }

    public abstract class PlanarYuvBuffer : VideoFrameBuffer
    {
        public int ChromaWidth => Info.Yuv.ChromaWidth;
        public int ChromaHeight => Info.Yuv.ChromaHeight;
        public int StrideY => Info.Yuv.StrideY;
        public int StrideU => Info.Yuv.StrideU;
        public int StrideV => Info.Yuv.StrideV;
        public IntPtr DataY => (IntPtr)Info.Yuv.DataYPtr;
        public IntPtr DataU => (IntPtr)Info.Yuv.DataUPtr;
        public IntPtr DataV => (IntPtr)Info.Yuv.DataVPtr;

        internal PlanarYuvBuffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
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
        public int ChromaWidth => Info.BiYuv.ChromaWidth;
        public int ChromaHeight => Info.BiYuv.ChromaHeight;
        public int StrideY => Info.BiYuv.StrideY;
        public int StrideUV => Info.BiYuv.StrideUv;
        public IntPtr DataY => (IntPtr)Info.BiYuv.DataYPtr;
        public IntPtr DataUV => (IntPtr)Info.BiYuv.DataUvPtr;

        internal BiplanarYuvBuffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public abstract class BiplanarYuv8Buffer : BiplanarYuvBuffer
    {
        internal BiplanarYuv8Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class NativeBuffer : VideoFrameBuffer
    {
        internal override long GetMemorySize()
        {
            return 0;
        }

        internal NativeBuffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }
    }

    public class I420Buffer : PlanarYuv8Buffer
    {
        internal I420Buffer(FFIHandle handle, VideoFrameBufferInfo info) : base(handle, info) { }

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
