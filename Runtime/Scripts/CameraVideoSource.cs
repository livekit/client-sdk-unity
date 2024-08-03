using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System;
using Unity.Collections;

namespace LiveKit
{
    public class CameraVideoSource : RtcVideoSource
    {
        private TextureFormat _textureFormat;

        public Camera Camera { get; }

        public override int GetWidth()
        {
            return Camera.pixelWidth;
        }

        public override int GetHeight()
        {
            return Camera.pixelHeight;
        }

        public CameraVideoSource(Camera camera,   VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Screen, bufferType)
        {
            Camera = camera;
            base.Init();
        }

        ~CameraVideoSource()
        {
            Dispose();
            ClearRenderTexture();
        }

        public override void Stop()
        {
            base.Stop();
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
                    Camera.targetTexture = _dest as RenderTexture;
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
    }
}

