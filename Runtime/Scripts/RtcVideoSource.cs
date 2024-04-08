using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using LiveKit.Internal.FFIClients.Requests;
using System.Collections;

namespace LiveKit
{
    public abstract class RtcVideoSource
    {
        public enum VideoStreamSource
        {
            Texture = 0,
            Screen = 1,
            Camera = 2
        }

        internal FfiHandle Handle { get; }

        public abstract int GetWidth();
        public abstract int GetHeight();

        protected Texture _dest;
        protected NativeArray<byte> _data;
        protected VideoStreamSource _sourceType;
        protected VideoBufferType _bufferType;
        protected VideoSourceInfo _info;
        protected bool _reading = false;
        protected bool _requestPending = false;
        protected bool isDisposed = true;
        protected bool _playing = false;

        public RtcVideoSource(VideoStreamSource sourceType, VideoBufferType bufferType)
        {
            isDisposed = false;
            _sourceType = sourceType;
            _bufferType = bufferType;
            using var request = FFIBridge.Instance.NewRequest<NewVideoSourceRequest>();
            var newVideoSource = request.request;
            newVideoSource.Type = VideoSourceType.VideoSourceNative;
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewVideoSource.Source.Info;
            Handle = FfiHandle.FromOwnedHandle(res.NewVideoSource.Source.Handle);
        }

        protected TextureFormat GetTextureFormat(VideoBufferType type)
        {
            switch (type)
            {
                case VideoBufferType.Rgba:
                    return TextureFormat.RGBA32;
                case VideoBufferType.Argb:
                    return TextureFormat.ARGB32;
                default:
                    throw new NotImplementedException("TODO: Add TextureFormat support for type: " + type);
            }
        }

        protected int GetStrideForBuffer(VideoBufferType type)
        {
            switch (type)
            {
                case VideoBufferType.Rgba:
                case VideoBufferType.Argb:
                    return 4;
                default:
                    throw new NotImplementedException("TODO: Add stride support for type: " + type);
            }
        }

        public virtual void Start()
        {
            Stop();
            _playing = true;
        }

        public virtual void Stop()
        {
            _playing = false; 
        }

        public IEnumerator Update()
        {
            while (_playing)
            {
                yield return null;
                ReadBuffer();
                SendFrame();
            }

            yield break;
        }

        public virtual void Dispose()
        {
            if (!isDisposed)
            { 
                _data.Dispose();
                isDisposed = true;
            }
        }

        protected abstract void ReadBuffer();

        protected virtual bool SendFrame()
        {
            var result = _requestPending && !isDisposed;
            if (result)
            {
                var buffer = new VideoBufferInfo();
                unsafe
                {
                    buffer.DataPtr = (ulong)NativeArrayUnsafeUtility.GetUnsafePtr(_data);
                }

                buffer.Type = _bufferType;
                buffer.Stride = (uint)GetWidth() * (uint)GetStrideForBuffer(_bufferType);
                buffer.Width = (uint)GetWidth();
                buffer.Height = (uint)GetHeight();

                // Send the frame to WebRTC
                using var request = FFIBridge.Instance.NewRequest<CaptureVideoFrameRequest>();
                var capture = request.request;
                capture.SourceHandle = (ulong)Handle.DangerousGetHandle();
                capture.Rotation = VideoRotation._0;
                capture.TimestampUs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                capture.Buffer = buffer;
                using var response = request.Send();
                _reading = false;
                _requestPending = false;
            }
            return result;
        }

        protected void OnReadback(AsyncGPUReadbackRequest req)
        {
            if (!req.hasError)
            {
                _requestPending = true;
            }
            else
            {
                Utils.Error("GPU Read Back on Video Source Failed: " + req.ToString());
                _reading = false;
            }
        }
    }
}

