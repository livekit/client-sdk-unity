using System;
using LiveKit.RtcSources.Video;
using RichTypes;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace Examples.CameraCapture
{
    public class CameraCapturePlayground : MonoBehaviour
    {
        [SerializeField]
        private RawImage output = null!;

        private WebCameraVideoInput? webCameraVideoInput;
        private Texture2D? outputTexture;

        private void Awake()
        {
            Assert.IsNotNull(output, "Output is null");
        }

        private void Start()
        {
            Result<WebCameraVideoInput> inputResult = WebCameraVideoInput.NewDefault();
            if (inputResult.Success == false)
            {
                Debug.LogError($"Cannot create video source: {inputResult.ErrorMessage}");
                return;
            }

            webCameraVideoInput = inputResult.Value;
            webCameraVideoInput.Start();
        }

        private void Update()
        {
            if (webCameraVideoInput == null)
            {
                return;
            }

            Option<Result<VideoInputFrame>> nextFrame = webCameraVideoInput.ConsumeFrame();
            if (nextFrame.Has)
            {
                Result<VideoInputFrame> result = nextFrame.Value;
                if (result.Success)
                {
                    ProcessFrame(result.Value);
                }
                else
                    Debug.LogError($"Error during reading video input frame: {result.ErrorMessage}");
            }
            else
            {
                webCameraVideoInput.StartReadNextFrameIfNot();
            }
        }

        private void ProcessFrame(VideoInputFrame frame)
        {
            (int width, int height) size = frame.size.AsInts();
            TextureFormat format = VideoUtils.TextureFormatFromVideoBufferType(frame.bufferType);

            if (outputTexture == null
                || outputTexture.width != size.width
                || outputTexture.height != size.height)
            {
                if (outputTexture != null)
                {
                    Destroy(outputTexture);
                    outputTexture = null;
                    output.texture = null!;
                }

                outputTexture = new Texture2D(size.width, size.height, format, mipChain: false);
                output.texture = outputTexture;
            }

            int outputLength = outputTexture.width
                               * outputTexture.height
                               * VideoUtils.BytesPerPixel(VideoUtils.RenderTextureFormatFrom(format));

            if (outputLength != frame.dataLength)
            {
                Debug.LogWarning(
                    $"Output required length {outputLength} is not equals to dataFrame length {frame.dataLength}, skipping the frame"
                );
                return;
            }

            // Only 64-bit platforms
            IntPtr ptr = unchecked((IntPtr)(long)frame.readOnlyPointer);
            outputTexture.LoadRawTextureData(ptr, frame.dataLength);
            outputTexture.Apply();
        }
    }
}