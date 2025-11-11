using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System;
using Unity.Collections;

namespace LiveKit
{

    // VideoSource for Unity Camera
    public class CameraVideoSource : RtcVideoSource
    {
        private TextureFormat _textureFormat;

        private RenderTexture _renderTexture;

        public Camera Camera { get; }

        public override int GetWidth()
        {
            return Camera.pixelWidth;
        }

        public override int GetHeight()
        {
            return Camera.pixelHeight;
        }

        protected override VideoRotation GetVideoRotation()
        {
            return VideoRotation._0;
        }

        public CameraVideoSource(Camera camera, VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Screen, bufferType)
        {
            Camera = camera;
            base.Init();
        }

        ~CameraVideoSource()
        {
            Dispose(false);
            ClearRenderTexture();
        }

        public override void Stop()
        {
            base.Stop();
            ClearRenderTexture();
        }

        private void ClearRenderTexture()
        {
            if (_renderTexture)
            {
                var renderText = _renderTexture as RenderTexture;
                renderText.Release(); // can only be done on main thread
            }
        }

        // Read the texture data into a native array asynchronously
        protected override bool ReadBuffer()
        {
            if (_reading)
                return false;
            _reading = true;
            var textureChanged = false;
            try
            {
                if (_renderTexture == null || _renderTexture.width != GetWidth() || _renderTexture.height != GetHeight())
                {
                    var targetFormat = Utils.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
                    var compatibleFormat = SystemInfo.GetCompatibleFormat(targetFormat, FormatUsage.ReadPixels);
                    _textureFormat = GraphicsFormatUtility.GetTextureFormat(compatibleFormat);
                    _bufferType = GetVideoBufferType(_textureFormat);
                    _renderTexture = new RenderTexture(GetWidth(), GetHeight(), 0, compatibleFormat);
                    Camera.targetTexture = _renderTexture as RenderTexture;
                    _captureBuffer = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(_bufferType), Allocator.Persistent);
                    _previewTexture = new RenderTexture(GetWidth(), GetHeight(), 0, compatibleFormat);
                    textureChanged = true;
                }

                ScreenCapture.CaptureScreenshotIntoRenderTexture(_renderTexture);

                Graphics.Blit(_renderTexture, _previewTexture, new Vector2(1f, -1f), new Vector2(0f, 1f));
                AsyncGPUReadback.RequestIntoNativeArray(ref _captureBuffer, _previewTexture, 0, _textureFormat, OnReadback);
            }
            catch (Exception e)
            {
                Utils.Error(e);
            }
            return textureChanged;
        }
    }
}

