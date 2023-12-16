using System;
using System.Collections;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LiveKit
{
    public abstract class RtcVideoSource
    {
        internal readonly FfiHandle Handle;
        protected VideoSourceInfo _info;

        public RtcVideoSource()
        {
            var newVideoSource = new NewVideoSourceRequest();
            newVideoSource.Type = VideoSourceType.VideoSourceNative;

            var request = new FFIRequest();
            request.NewVideoSource = newVideoSource;

            var resp = FfiClient.SendRequest(request);
            _info = resp.NewVideoSource.Source;
            Handle = new FfiHandle((IntPtr)_info.Handle.Id);
        }
    }

    public class TextureVideoSource : RtcVideoSource
    {
        public Texture Texture { get; }
        private NativeArray<byte> _data;
        private bool _reading = false;

        public TextureVideoSource(Texture texture)
        {
            Texture = texture;
            _data = new NativeArray<byte>(Texture.width * Texture.height * 4, Allocator.Persistent);
        }

        ~TextureVideoSource()
        {
            if (_data != null)
            {
                _data.Dispose();
                _data = null;
            }
        }

        // Read the texture data into a native array asynchronously
        internal void ReadBuffer()
        {
            if (_reading)
                return;

            _reading = true;
            AsyncGPUReadback.RequestIntoNativeArray(ref _data, Texture, 0, TextureFormat.RGBA32, OnReadback);
        }

        public IEnumerator Update()
        {
            while (true)
            {
                yield return null;
                ReadBuffer();

            }
        }

        private void OnReadback(AsyncGPUReadbackRequest req)
        {
            _reading = false;
            if (req.hasError)
            {
                Utils.Error("failed to read texture data");
                return;
            }

            // ToI420
            var argbInfo = new ARGBBufferInfo();
            unsafe
            {
                argbInfo.Ptr = (ulong)NativeArrayUnsafeUtility.GetUnsafePtr(_data);
            }
            argbInfo.Format = VideoFormatType.FormatArgb;
            argbInfo.Stride = (uint)Texture.width * 4;
            argbInfo.Width = (uint)Texture.width;
            argbInfo.Height = (uint)Texture.height;

            var toI420 = new ToI420Request();
            toI420.FlipY = true;
            toI420.Argb = argbInfo;

            var request = new FFIRequest();
            request.ToI420 = toI420;

            var resp = FfiClient.SendRequest(request);
            var bufferInfo = resp.ToI420.Buffer;
            var buffer = VideoFrameBuffer.Create(new FfiHandle((IntPtr)bufferInfo.Handle.Id), bufferInfo);

            // Send the frame to WebRTC
            var frameInfo = new VideoFrameInfo();
            frameInfo.Rotation = VideoRotation._0;
            frameInfo.Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            var capture = new CaptureVideoFrameRequest();
            capture.SourceHandle = new FFIHandleId { Id = (ulong)Handle.DangerousGetHandle() };
            capture.BufferHandle = new FFIHandleId { Id = (ulong)buffer.Handle.DangerousGetHandle() };
            capture.Frame = frameInfo;

            request = new FFIRequest();
            request.CaptureVideoFrame = capture;

            FfiClient.SendRequest(request);
        }
    }
}
