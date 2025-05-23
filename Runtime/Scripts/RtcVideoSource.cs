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
    public abstract class RtcVideoSource : IRtcSource, IDisposable
    {
        public enum VideoStreamSource
        {
            Texture = 0,
            Screen = 1,
            Camera = 2
        }

        internal FfiHandle Handle { get; set; }

        public abstract int GetWidth();
        public abstract int GetHeight();

        protected abstract VideoRotation GetVideoRotation();

        public delegate void TextureReceiveDelegate(Texture2D tex2d);
        /// Called when we receive a new texture (first texture or the resolution changed)
        public event TextureReceiveDelegate TextureReceived;

        protected Texture2D _previewTexture;
        protected NativeArray<byte> _captureBuffer;
        protected VideoStreamSource _sourceType;
        protected VideoBufferType _bufferType;
        protected VideoSourceInfo _info;
        protected bool _reading = false;
        protected bool _requestPending = false;
        protected bool _disposed = false;
        protected bool _playing = false;
        private bool _muted = false;
        public override bool Muted => _muted;

        internal RtcVideoSource(VideoStreamSource sourceType, VideoBufferType bufferType)
        {
            _sourceType = sourceType;
            _bufferType = bufferType;
            Handle = null;
        }

        protected void Init()
        {
            if (Handle == null)
            {
                using var request = FFIBridge.Instance.NewRequest<NewVideoSourceRequest>();
                var newVideoSource = request.request;
                newVideoSource.Resolution = request.TempResource<VideoSourceResolution>();
                newVideoSource.Resolution.Width = (uint)GetWidth();
                newVideoSource.Resolution.Height = (uint)GetHeight();
                newVideoSource.Type = VideoSourceType.VideoSourceNative;
                using var response = request.Send();
                FfiResponse res = response;
                _info = res.NewVideoSource.Source.Info;
                Handle = FfiHandle.FromOwnedHandle(res.NewVideoSource.Source.Handle);
            }
        }

        protected TextureFormat GetTextureFormat(VideoBufferType type)
        {
            switch (type)
            {
                case VideoBufferType.Rgba:
                    return TextureFormat.RGBA32;
                case VideoBufferType.Argb:
                    return TextureFormat.ARGB32;
                case VideoBufferType.Bgra:
                    return TextureFormat.BGRA32;
                case VideoBufferType.Rgb24:
                    return TextureFormat.RGB24;
                default:
                    throw new NotImplementedException("TODO: Add TextureFormat support for type: " + type);
            }
        }

        protected VideoBufferType GetVideoBufferType(TextureFormat type)
        {
            switch (type)
            {
                case  TextureFormat.RGBA32:
                    return VideoBufferType.Rgba;
                case TextureFormat.ARGB32:
                    return VideoBufferType.Argb;
                case TextureFormat.BGRA32:
                    return VideoBufferType.Bgra;
                case TextureFormat.RGB24:
                    return VideoBufferType.Rgb24;
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
                case VideoBufferType.Bgra:
                    return 4;
                case VideoBufferType.Rgb24:
                    return 3;
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
                if (_disposed) break;
                var textureChanged = ReadBuffer();

                if (textureChanged)
                    TextureReceived?.Invoke(_previewTexture);

                if (_muted)continue;
                SendFrame();
            }

            yield break;
        }

        public override void SetMute(bool muted)
        {
            _muted = muted;
        }

        protected abstract bool ReadBuffer();

        protected virtual bool SendFrame()
        {
            var result = _requestPending && !_disposed;
            if (result)
            {
                var buffer = new VideoBufferInfo();
                unsafe
                {
                    buffer.DataPtr = (ulong)NativeArrayUnsafeUtility.GetUnsafePtr(_captureBuffer);
                }

                buffer.Type = _bufferType;
                buffer.Stride = (uint)GetWidth() * (uint)GetStrideForBuffer(_bufferType);
                buffer.Width = (uint)GetWidth();
                buffer.Height = (uint)GetHeight();

                // Send the frame to WebRTC
                using var request = FFIBridge.Instance.NewRequest<CaptureVideoFrameRequest>();
                var capture = request.request;
                capture.SourceHandle = (ulong)Handle.DangerousGetHandle();
                capture.Rotation = GetVideoRotation();
                var now = DateTimeOffset.UtcNow;
                capture.TimestampUs = now.ToUnixTimeMilliseconds() * 1000 + (now.Ticks % TimeSpan.TicksPerMillisecond) / 10;
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (_previewTexture != null)
                 UnityEngine.Object.Destroy(_previewTexture);
            if (_captureBuffer.IsCreated)
            {
                Debug.Log("Disposing capture buffer");
                _captureBuffer.Dispose();
            }
            _disposed = true;
        }

        ~RtcVideoSource()
        {
            Dispose(false);
        }
    }
}

