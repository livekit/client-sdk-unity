#if !UNITY_WEBGL

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiveKit.Proto;
using LiveKit.Internal;
using Unity.Collections.LowLevel.Unsafe;
using LiveKit.client_sdk_unity.Runtime.Scripts.Internal.FFIClients;
using LiveKit.Internal.FFIClients;
using RichTypes;
using Unity.Collections;

namespace LiveKit.RtcSources.Video
{
    public interface IVideoInput : IDisposable
    {
        void Start();

        void Stop();
        
        VideoSize Size();

        void StartReadNextFrameIfNot();

        Option<Result<VideoInputFrame>> ConsumeFrame();
    }

    public readonly struct VideoInputFrame
    {
        /// <summary>
        /// Valid until next texture read, consume immediately
        /// </summary>
        public readonly ulong readOnlyPointer;
        public readonly int dataLength;
        public readonly VideoBufferType bufferType;
        public readonly VideoSize size;
        public readonly uint stride;

        public VideoInputFrame(NativeArray<byte>.ReadOnly data, VideoBufferType bufferType, VideoSize size)
        {
            unsafe
            {
                readOnlyPointer = (ulong)data.GetUnsafeReadOnlyPtr()!;
            }

            dataLength = data.Length;
            this.bufferType = bufferType;
            this.size = size;
            stride = size.width * (uint)VideoUtils.StrideFromVideoBufferType(bufferType);
        }
    }

    /// <summary>
    /// Main thread only API (background work is handled under the hood)
    /// </summary>
    public class RtcVideoSource : IDisposable
    {
        private readonly IVideoInput videoInput;
        private readonly FfiHandle handle;

        private bool isDisposed;

        private CancellationTokenSource? runningCancellationTokenSource;

        public ulong HandleId => (ulong)handle.DangerousGetHandle();

        public RtcVideoSource(IVideoInput videoInput)
        {
            this.videoInput = videoInput;
            using FfiRequestWrap<NewVideoSourceRequest> request = FFIBridge.Instance.NewRequest<NewVideoSourceRequest>();
            NewVideoSourceRequest newVideoSource = request.request;
            newVideoSource.Type = VideoSourceType.VideoSourceNative;
            using FfiResponseWrap response = request.Send();
            FfiResponse res = response;
            handle = IFfiHandleFactory.Default.NewFfiHandle(res.NewVideoSource!.Source!.Handle!.Id);
        }

        public void Start()
        {
            if (runningCancellationTokenSource is { IsCancellationRequested: false })
                return;

            videoInput.Start();
            runningCancellationTokenSource = new CancellationTokenSource();
            UpdateLoopAsync(runningCancellationTokenSource.Token).Forget();
        }

        public void Stop()
        {
            videoInput.Stop();
            runningCancellationTokenSource?.Cancel();
            runningCancellationTokenSource?.Dispose();
            runningCancellationTokenSource = null;
        }

        private async UniTaskVoid UpdateLoopAsync(CancellationToken token)
        {
            if (isDisposed) return;

            while (token.IsCancellationRequested == false)
            {
                Option<Result<VideoInputFrame>> nextFrame = videoInput.ConsumeFrame();
                if (nextFrame.Has)
                {
                    Result<VideoInputFrame> result = nextFrame.Value;
                    if (result.Success)
                    {
                        await UniTask.SwitchToThreadPool();
                        ProcessFrame(result.Value);
                        await UniTask.SwitchToMainThread();
                    }
                    else
                        Utils.Error($"Error during reading video input frame: {result.ErrorMessage}");
                }
                else
                {
                    videoInput.StartReadNextFrameIfNot();
                }

                await UniTask.Yield(timing: PlayerLoopTiming.Update);
            }
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            Stop();
            videoInput.Dispose();
        }

        private void ProcessFrame(VideoInputFrame frame)
        {
            using FfiRequestWrap<CaptureVideoFrameRequest> requestWrap =
                FFIBridge.Instance.NewRequest<CaptureVideoFrameRequest>();

            using SmartWrap<VideoBufferInfo> bufferWrap =
                requestWrap.TempResource<VideoBufferInfo>();

            VideoBufferInfo buffer = bufferWrap.value;
            buffer.DataPtr = frame.readOnlyPointer;
            buffer.Type = frame.bufferType;
            buffer.Stride = frame.stride;
            buffer.Width = frame.size.width;
            buffer.Height = frame.size.height;

            // Send the frame to WebRTC
            CaptureVideoFrameRequest capture = requestWrap.request;
            capture.SourceHandle = (ulong)handle.DangerousGetHandle();
            capture.Rotation = VideoRotation._0;
            capture.TimestampUs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            capture.Buffer = buffer;
            // TODO await until real send result??
            using FfiResponseWrap response = requestWrap.Send();
            // TODO test zerofy the buffer after sending to validate lifetime of the passed buffer
        }
    }
}

#endif
