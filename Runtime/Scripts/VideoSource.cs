using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using System.Collections;

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
            Handle = new FfiHandle((IntPtr)res.NewVideoSource.Source.Handle.Id);
        }
    }

    public class TextureVideoSource : RtcVideoSource
    {
        public Texture Texture { get; }
        private NativeArray<byte> _data;
        private bool _reading = false;
        private bool isDisposed = true;
        private Thread? readVideoThread;
        private AsyncGPUReadbackRequest _readBackRequest;
        private bool _requestPending = false;

        public TextureVideoSource(Texture texture)
        {
            Texture = texture;
            _data = new NativeArray<byte>(Texture.width * Texture.height * 4, Allocator.Persistent);
            isDisposed = false;
        }

        ~TextureVideoSource()
        {
            if (!isDisposed)
            {
                _data.Dispose();
                isDisposed = true;
            }
        }

        public void Start()
        {
            Stop();
            //readVideoThread = new Thread(Update);
            //readVideoThread.Start();
        }

        public void Stop()
        {
            //readVideoThread?.Abort();
        }

        // Read the texture data into a native array asynchronously
        internal void ReadBuffer()
        {
            if (_reading)
                return;

            _reading = true;
            try
            {
                AsyncGPUReadback.RequestIntoNativeArray(ref _data, Texture, 0, TextureFormat.RGBA32, OnReadback);
            } catch(Exception _)
            {
                _reading = false;
            }
            
        }

        public IEnumerator Update()
        {
            while (true)
            {
                yield return null;
                ReadBuffer();
                ReadBack();

            }
        }

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
                argbInfo.Type = VideoBufferType.Rgba;
                argbInfo.Stride = (uint)Texture.width * 4;
                argbInfo.Width = (uint)Texture.width;
                argbInfo.Height = (uint)Texture.height;

                using var requestToI420 = FFIBridge.Instance.NewRequest<VideoConvertRequest>();
                var toI420 = requestToI420.request;
                toI420.FlipY = true;
                toI420.Buffer = argbInfo;
                toI420.DstType = VideoBufferType.I420;

                using var responseToI420 = requestToI420.Send();
                FfiResponse res = responseToI420;

                using var request = FFIBridge.Instance.NewRequest<CaptureVideoFrameRequest>();
                var capture = request.request;
                capture.Buffer = res.VideoConvert.Buffer.Info;
                capture.TimestampUs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                capture.Rotation = VideoRotation._0;
                capture.SourceHandle = (ulong)Handle.DangerousGetHandle();
                using var response = request.Send();

                _reading = false;
                _requestPending = false;
            }
        }
    }
}
