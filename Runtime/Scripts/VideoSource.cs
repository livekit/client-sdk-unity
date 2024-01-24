using System;
using System.Collections;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;
using System.Threading;
using UnityEngine.UIElements.Experimental;

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

                var toI420 = new VideoConvertRequest();
                toI420.FlipY = true;
                toI420.Buffer = argbInfo;
                toI420.DstType = VideoBufferType.I420;

                var request = new FfiRequest();
                request.VideoConvert = toI420;

                var resp = FfiClient.SendRequest(request);
                var newBuffer = resp.VideoConvert.Buffer;

                var capture = new CaptureVideoFrameRequest();
                capture.Buffer = newBuffer.Info;
                capture.TimestampUs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                capture.Rotation = VideoRotation._0;
                capture.SourceHandle = (ulong)Handle.DangerousGetHandle();


                request = new FfiRequest();
                request.CaptureVideoFrame = capture;

                FfiClient.SendRequest(request);
                _reading = false;
                _requestPending = false;
            }
        }
    }
}
