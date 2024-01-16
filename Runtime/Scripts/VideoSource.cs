using System;
using System.Collections;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;

namespace LiveKit
{
    public abstract class RtcVideoSource
    {
        //internal readonly FfiHandle Handle;
        private FfiHandle _handle;
        internal FfiHandle Handle
        {
            get { return _handle; }
        }
        protected VideoSourceInfo _info;

        public RtcVideoSource()
        {
            
        }

        async void Init(CancellationToken canceltoken)
        {
            if (canceltoken.IsCancellationRequested)
            {
                // End the task
                Utils.Debug("Task cancelled");
                return;
            }
            var newVideoSource = new NewVideoSourceRequest();
            newVideoSource.Type = VideoSourceType.VideoSourceNative;

            var request = new FfiRequest();
            request.NewVideoSource = newVideoSource;

            var resp = await FfiClient.SendRequest(request);
            // Check if the task has been cancelled
            if (canceltoken.IsCancellationRequested)
            {
                // End the task
                Utils.Debug("Task cancelled");
                return;
            }
            _info = resp.NewVideoSource.Source.Info;
            _handle = new FfiHandle((IntPtr)resp.NewVideoSource.Source.Handle.Id);
        }
    }

    public class TextureVideoSource : RtcVideoSource
    {
        public Texture Texture { get; }
        private NativeArray<byte> _data;
        private bool _reading = false;
        private bool isDisposed = true;
        private CancellationToken canceltoken;

        public TextureVideoSource(Texture texture)
        {
            Texture = texture;
            _data = new NativeArray<byte>(Texture.width * Texture.height * 4, Allocator.Persistent);
            isDisposed = false;
        }

        public void Init(CancellationToken canceltoken)
        {
            this.canceltoken = canceltoken;
            Thread sendDataThread = new Thread(new ThreadStart(StartReading));
            sendDataThread.Start();
        }

        ~TextureVideoSource()
        {
            if (!isDisposed)
            {
                _data.Dispose();
                isDisposed = true;
            }
        }

        // Read the texture data into a native array asynchronously
        internal void ReadBuffer()
        {
            if (canceltoken.IsCancellationRequested)
            {
                // End the task
                Utils.Debug("Task cancelled");
                return;
            }
            if (_reading)
                return;

            _reading = true;
            AsyncGPUReadback.RequestIntoNativeArray(ref _data, Texture, 0, TextureFormat.RGBA32, OnReadback);
        }

        private void StartReading()
        {
            while (true)
            {
                //yield return null;
                ReadBuffer();
                if (canceltoken.IsCancellationRequested)
                {
                    // End the task
                    Utils.Debug("Task cancelled");
                    break;
                }
            }
        }

        async private void OnReadback(AsyncGPUReadbackRequest req)
        {
            if (canceltoken.IsCancellationRequested)
            {
                // End the task
                Utils.Debug("Task cancelled");
                return;
            }
            _reading = false;
            if (req.hasError)
            {
                Utils.Error("failed to read texture data");
                return;
            }

            // ToI420
            var argbInfo = new ArgbBufferInfo();
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

            var request = new FfiRequest();
            request.ToI420 = toI420;

            var resp = await FfiClient.SendRequest(request);
            // Check if the task has been cancelled
            if (canceltoken.IsCancellationRequested)
            {
                // End the task
                Utils.Debug("Task cancelled");
                return;
            }
            var bufferInfo = resp.ToI420.Buffer;
            var buffer = VideoFrameBuffer.Create(new FfiHandle((IntPtr)bufferInfo.Handle.Id), bufferInfo.Info);

            // Send the frame to WebRTC
            var frameInfo = new VideoFrameInfo();
            frameInfo.Rotation = VideoRotation._0;
            frameInfo.TimestampUs = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            var capture = new CaptureVideoFrameRequest();
            capture.SourceHandle = (ulong)Handle.DangerousGetHandle();
            capture.Handle = (ulong)buffer.Handle.DangerousGetHandle();
            capture.Frame = frameInfo;

            request = new FfiRequest();
            request.CaptureVideoFrame = capture;

            FfiClient.SendRequest(request);
        }
    }
}
