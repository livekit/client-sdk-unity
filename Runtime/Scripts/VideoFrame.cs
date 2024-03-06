using System;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Internal.FFIClients.Requests;
using UnityEngine;
using UnityEngine.Rendering;

namespace LiveKit
{
    public class VideoFrame : IDisposable
    {
        private VideoBufferInfo _info;
        internal VideoBufferInfo Info => _info;

        private FfiHandle _handle;
        public FfiHandle Handle => _handle;
        internal uint Width => _info.Width;
        internal uint Height => _info.Height;
        internal uint Stride => _info.Stride;
        private VideoBufferType _type;
        internal VideoBufferType Type => _type;
      
        private bool _disposed = false;

        public bool IsValid => !Handle.IsClosed && !Handle.IsInvalid;

        // Explicitly ask for FFIHandle 
        protected VideoFrame(FfiHandle handle, VideoBufferInfo info)
        {
            _info = info;
            _handle = handle;
            
            _type = info.Type;
            var memSize = GetMemorySize();
            if (memSize > 0)
                GC.AddMemoryPressure(memSize);
        }


     

        


        ~VideoFrame()
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
        public virtual long GetMemorySize()
        {
            return  Height * Stride;
        }

        public static VideoFrame FromOwnedInfo(OwnedVideoBuffer ownedInfo)
        {
            var info = ownedInfo.Info;
            VideoFrame frame = new VideoFrame(IFfiHandleFactory.Default.NewFfiHandle(info.DataPtr), info);
            return frame;
        }

        public static VideoFrame Convert(OwnedVideoBuffer ownedInfo, VideoBufferType type)
        {
            using var request = FFIBridge.Instance.NewRequest<VideoConvertRequest>();
            var alloc = request.request;
            alloc.FlipY = GetFlip();
            alloc.DstType = type;
            alloc.Buffer = ownedInfo.Info;
            using var response = request.Send();
            FfiResponse res = response;
            if(res.VideoConvert.HasError)
            {
                throw new Exception(res.VideoConvert.Error);
            }
            return FromOwnedInfo(res.VideoConvert.Buffer);
        }

       
        protected static bool GetFlip()
        {
            var graphicDevice = SystemInfo.graphicsDeviceType;
            return graphicDevice == GraphicsDeviceType.OpenGLCore ||
            graphicDevice == GraphicsDeviceType.OpenGLES2 ||
            graphicDevice == GraphicsDeviceType.OpenGLES3 ||
            graphicDevice == GraphicsDeviceType.Vulkan ?
            false :
            true;
        }
    }
}