using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    public abstract class RtcVideoSource
    {
        internal FfiHandle Handle { get; }

        protected VideoSourceInfo _info;

        public RtcVideoSource()
        {
            using var request = FFIBridge.Instance.NewRequest<NewVideoSourceRequest>();
            var newVideoSource = request.request;
            newVideoSource.Type = VideoSourceType.VideoSourceNative;
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewVideoSource.Source.Info;
            Handle = IFfiHandleFactory.Default.NewFfiHandle(res.NewVideoSource.Source.Handle.Id);
        }
    }

    public class TextureVideoSource : RtcVideoSource
    {
        public Texture Texture { get; }
        private NativeArray<byte> data;
        private bool reading = false;
        private bool isDisposed = true;
        private Thread? readVideoThread;

        public TextureVideoSource(Texture texture)
        {
            Texture = texture;
            data = new NativeArray<byte>(Texture.width * Texture.height * 4, Allocator.Persistent);
            isDisposed = false;
        }

        public void Start()
        {
            Stop();
            readVideoThread = new Thread(Update);
            readVideoThread.Start();
        }

        public void Stop()
        {
            readVideoThread?.Abort();
        }

        ~TextureVideoSource()
        {
            if (!isDisposed)
            {
                data.Dispose();
                isDisposed = true;
            }
        }


        private void Update()
        {
            while (true)
            {
                Thread.Sleep(Constants.TASK_DELAY);
                ReadBuffer();
                ReadBack();
            }
        }

        // Read the texture data into a native array asynchronously
        internal void ReadBuffer()
        {
            if (reading)
                return;

            reading = true;
            AsyncGPUReadback.RequestIntoNativeArray(ref data, Texture, 0, TextureFormat.RGBA32, OnReadback);
        }

        private AsyncGPUReadbackRequest _readBackRequest;
        private bool _requestPending = false;

        private void OnReadback(AsyncGPUReadbackRequest req)
        {
            _readBackRequest = req;
            _requestPending = true;
        }

        private void ReadBack()
        {
            if (_requestPending && !isDisposed)
            {
                var req = _readBackRequest;
                if (req.hasError)
                {
                    Utils.Error("failed to read texture data");
                    return;
                }

                // ToI420

                using var requestToI420 = FFIBridge.Instance.NewRequest<ToI420Request>();
                using var argbInfoWrap = requestToI420.TempResource<ArgbBufferInfo>();

                var argbInfo = argbInfoWrap.value;
                unsafe
                {
                    argbInfo.Ptr = (ulong)NativeArrayUnsafeUtility.GetUnsafePtr(data);
                }

                argbInfo.Format = VideoFormatType.FormatArgb;
                argbInfo.Stride = (uint)Texture.width * 4;
                argbInfo.Width = (uint)Texture.width;
                argbInfo.Height = (uint)Texture.height;

                var toI420 = requestToI420.request;
                toI420.FlipY = true;
                toI420.Argb = argbInfo;
                using var responseToI420 = requestToI420.Send();
                FfiResponse res = responseToI420;

                var bufferInfo = res.ToI420.Buffer;
                var buffer = VideoFrameBuffer.Create(IFfiHandleFactory.Default.NewFfiHandle(bufferInfo.Handle.Id), bufferInfo.Info);

                // Send the frame to WebRTC

                using var request = FFIBridge.Instance.NewRequest<CaptureVideoFrameRequest>();
                using var frameInfoWrap = request.TempResource<VideoFrameInfo>();
                
                var frameInfo = frameInfoWrap.value;
                frameInfo.Rotation = VideoRotation._0;
                frameInfo.TimestampUs = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                var capture = request.request;
                capture.SourceHandle = (ulong)Handle.DangerousGetHandle();
                capture.Handle = (ulong)buffer.Handle.DangerousGetHandle();
                capture.Frame = frameInfo;
                using var response = request.Send();
                reading = false;
                _requestPending = false;
            }
        }
    }
}