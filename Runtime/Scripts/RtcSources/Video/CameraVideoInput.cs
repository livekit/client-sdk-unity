using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using LiveKit.Proto;
using RichTypes;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LiveKit.RtcSources.Video
{
    public class WebCameraVideoInput : IVideoInput
    {
        private readonly WebCamTexture webCamTexture;
        private readonly VideoBufferType bufferType;
        private readonly ReadVideoFrameTarget readVideoFrameTarget;

        public WebCameraVideoInput(WebCamTexture webCamTexture, VideoBufferType bufferType)
        {
            this.webCamTexture = webCamTexture;
            this.bufferType = bufferType;

            (int width, int height) = Size().AsInts();

            int length = width * height * VideoUtils.StrideFromVideoBufferType(bufferType);
            NativeArray<byte> capturedFrameData = new(length, Allocator.Persistent);
            TextureFormat textureFormat = VideoUtils.TextureFormatFromVideoBufferType(bufferType);
            readVideoFrameTarget = new ReadVideoFrameTarget(capturedFrameData, textureFormat, bufferToRenderTexture: true);
        }

        public static Result<WebCameraVideoInput> NewDefault()
        {
            WebCamDevice[] devices = WebCamTexture.devices!;
            if (devices.Length == 0)
                return Result<WebCameraVideoInput>.ErrorResult($"No device available at the time");
            string device = devices[0].name!;
            return NewFromDevice(device, VideoBufferType.Rgba);
        }

        public static Result<WebCameraVideoInput> NewFromDevice(string device, VideoBufferType bufferType)
        {
            WebCamDevice[] devices = WebCamTexture.devices!;
            if (devices.Any(d => d.name == device) == false)
                return Result<WebCameraVideoInput>.ErrorResult($"No device available with name: {device}");
            WebCamTexture webCam;
            try
            {
                webCam = new WebCamTexture(device);
            }
            catch (Exception e)
            {
                return Result<WebCameraVideoInput>.ErrorResult($"Cannot create webcam texture: {e.Message}");
            }

            WebCameraVideoInput input = new WebCameraVideoInput(webCam, bufferType);
            return Result<WebCameraVideoInput>.SuccessResult(input);
        }

        public void Dispose()
        {
            webCamTexture.Stop();
            DestroyAsync().Forget();
            return;

            async UniTaskVoid DestroyAsync()
            {
                await UniTask.SwitchToMainThread();
                Object.Destroy(webCamTexture);
            }
        }

        public void Start()
        {
            webCamTexture.Play();
        }

        public void Stop()
        {
            webCamTexture.Stop();
        }

        public VideoSize Size()
        {
            return new VideoSize((uint)webCamTexture.width, (uint)webCamTexture.height);
        }

        /// <summary>
        /// Main thread only
        /// </summary>
        public void StartReadNextFrameIfNot()
        {
            readVideoFrameTarget.StartIfNot(webCamTexture);
        }

        /// <summary>
        /// Main thread only
        /// </summary>
        public Option<Result<VideoInputFrame>> ConsumeFrame()
        {
            Option<Result<NativeArray<byte>.ReadOnly>> option = readVideoFrameTarget.Consume();
            if (option.Has == false)
                return Option<Result<VideoInputFrame>>.None;

            Result<NativeArray<byte>.ReadOnly> result = option.Value;
            Result<VideoInputFrame> outResult;
            if (result.Success == false)
            {
                outResult = Result<VideoInputFrame>.ErrorResult(result.ErrorMessage!);
                return Option<Result<VideoInputFrame>>.Some(outResult);
            }

            var frame = new VideoInputFrame(result.Value, bufferType, Size());
            outResult = Result<VideoInputFrame>.SuccessResult(frame);
            return Option<Result<VideoInputFrame>>.Some(outResult);
        }
    }

    public class ReadVideoFrameTarget : IDisposable
    {
        private enum Status
        {
            Awake,
            Reading,
            Ready,
            Error
        }

        private NativeArray<byte> capturedFrameData;
        private readonly TextureFormat textureFormat;
        private readonly Action<AsyncGPUReadbackRequest> callback;
        private readonly bool bufferToRenderTexture;
        private Status status;
        private string? lastError;
        private RenderTexture? bufferTexture;

        public ReadVideoFrameTarget(
            NativeArray<byte> capturedFrameData,
            TextureFormat textureFormat,
            bool bufferToRenderTexture
        )
        {
            this.capturedFrameData = capturedFrameData;
            this.textureFormat = textureFormat;
            this.bufferToRenderTexture = bufferToRenderTexture;
            status = Status.Awake;
            callback = ReadCallback;
        }

        public void StartIfNot(Texture texture)
        {
            if (status is not Status.Awake)
                return;

            if (bufferToRenderTexture)
            {
                if (bufferTexture == null
                    // Additional checks for width/height equality is needed because mac's camera at first call gives 16x16 pixels instead of real resolution
                    || bufferTexture.width != texture.width
                    || bufferTexture.height != texture.height)
                {
                    RenderTextureFormat format = VideoUtils.RenderTextureFormatFrom(textureFormat);
                    bufferTexture = new RenderTexture(texture.width, texture.height, 0, format);
                    bufferTexture.enableRandomWrite = true;
                    bufferTexture.Create();

                    var bytesPerPixel = VideoUtils.BytesPerPixel(format);
                    var requiredSize = texture.width * texture.height * bytesPerPixel;
                    if (capturedFrameData.IsCreated == false || capturedFrameData.Length != requiredSize)
                    {
                        if (capturedFrameData.IsCreated)
                            capturedFrameData.Dispose();

                        capturedFrameData = new NativeArray<byte>(requiredSize, Allocator.Persistent);
                    }
                }

                Graphics.Blit(texture, bufferTexture);
            }

            status = Status.Reading;
            AsyncGPUReadbackRequest _ = AsyncGPUReadback.RequestIntoNativeArray(
                ref capturedFrameData,
                bufferToRenderTexture ? bufferTexture! : texture,
                0,
                textureFormat,
                callback
            );
        }

        public Option<Result<NativeArray<byte>.ReadOnly>> Consume()
        {
            switch (status)
            {
                case Status.Awake:
                case Status.Reading:
                    return Option<Result<NativeArray<byte>.ReadOnly>>.None;
                case Status.Ready:
                    status = Status.Awake;
                    Result<NativeArray<byte>.ReadOnly> result =
                        Result<NativeArray<byte>.ReadOnly>.SuccessResult(capturedFrameData.AsReadOnly());
                    return Option<Result<NativeArray<byte>.ReadOnly>>.Some(result);
                case Status.Error:
                    status = Status.Awake;
                    result = Result<NativeArray<byte>.ReadOnly>.ErrorResult($"Error reading frame: {lastError}");
                    return Option<Result<NativeArray<byte>.ReadOnly>>.Some(result);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ReadCallback(AsyncGPUReadbackRequest obj)
        {
            status = obj.hasError ? Status.Error : Status.Ready;
            if (obj.hasError) lastError = obj.ToString();
        }

        public void Dispose()
        {
            if (capturedFrameData.IsCreated)
            {
                capturedFrameData.Dispose();
            }

            if (bufferTexture)
            {
                bufferTexture!.Release();
            }
        }
    }

}