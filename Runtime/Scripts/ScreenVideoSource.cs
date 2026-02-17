using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;

namespace LiveKit
{
    public class ScreenVideoSource : RtcVideoSource
    {
        private TextureFormat _textureFormat;
        private RenderTexture _renderTexture;

        public override int GetWidth()
        {
            return Screen.width;
        }

        public override int GetHeight()
        {
            return Screen.height;
        }

        protected override VideoRotation GetVideoRotation()
        {
            return VideoRotation._0;
        }

        public ScreenVideoSource(VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Screen, bufferType)
        {
            base.Init();
        }

        public override void Stop()
        {
            base.Stop();
            ClearRenderTexture();
        }

        ~ScreenVideoSource()
        {
            Dispose(false);
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

        protected override bool SendFrame()
        {
            var result = base.SendFrame();
            if (result)
            {
                ClearRenderTexture();
            }
            return result;
        }
    }
}
