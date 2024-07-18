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

        public override int GetWidth()
        {
            return Screen.width;
        }

        public override int GetHeight()
        {
            return Screen.height;
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
            Dispose();
            ClearRenderTexture();
        }

        private void ClearRenderTexture()
        {
            if (_dest)
            {
                var renderText = _dest as RenderTexture;
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
                if (_dest == null || _dest.width != GetWidth() || _dest.height != GetHeight())
                {
                    var targetFormat = Utils.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
                    var compatibleFormat = SystemInfo.GetCompatibleFormat(targetFormat, FormatUsage.ReadPixels);
                    _textureFormat = GraphicsFormatUtility.GetTextureFormat(compatibleFormat);
                    _bufferType = GetVideoBufferType(_textureFormat);
                    _dest = new RenderTexture(GetWidth(), GetHeight(), 0, compatibleFormat);
                    _data = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(_bufferType), Allocator.Persistent);
                    textureChanged = true;
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_dest as RenderTexture);
                AsyncGPUReadback.RequestIntoNativeArray(ref _data, _dest, 0, _textureFormat, OnReadback);
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

