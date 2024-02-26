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

            var request = new FfiRequest();
            request.NewVideoSource = newVideoSource;

            var resp = FfiClient.SendRequest(request);
            _info = resp.NewVideoSource.Source.Info;
            Handle = new FfiHandle((IntPtr)resp.NewVideoSource.Source.Handle.Id);
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
            var argbInfo = new VideoBufferInfo();
            unsafe
            {
                argbInfo.DataPtr = (ulong)NativeArrayUnsafeUtility.GetUnsafePtr(_data);
            }
            argbInfo.Type = VideoBufferType.Argb;
            argbInfo.Stride = (uint)Texture.width * 4;
            argbInfo.Width = (uint)Texture.width;
            argbInfo.Height = (uint)Texture.height;

            var toI420 = new VideoConvertRequest();
            toI420.FlipY = true;
            toI420.Buffer = argbInfo;

            var request = new FfiRequest();
            request.VideoConvert = toI420;

            var resp = FfiClient.SendRequest(request);
            var newBuffer = resp.VideoConvert.Buffer;
            var bufferInfo = newBuffer.Info;
            var buffer = VideoFrameBuffer.Create(new FfiHandle((IntPtr)newBuffer.Handle.Id), bufferInfo);

            // Send the frame to WebRTC
            var frameInfo = new VideoBufferInfo();

            var capture = new CaptureVideoFrameRequest();
            capture.Buffer = frameInfo;
            capture.TimestampUs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            capture.Rotation = VideoRotation._0;


            request = new FfiRequest();
            request.CaptureVideoFrame = capture;

            FfiClient.SendRequest(request);
        }
    }
}
