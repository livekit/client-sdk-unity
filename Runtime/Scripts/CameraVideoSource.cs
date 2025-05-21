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

        public CameraVideoSource(Camera camera,   VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Screen, bufferType)
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
                    _previewTexture = new Texture2D(GetWidth(), GetHeight(), _textureFormat, false);
                    textureChanged = true;
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_renderTexture);

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                // Flip the texture for OSX
                Graphics.CopyTexture(_renderTexture, _previewTexture);
                var pixels = _previewTexture.GetPixels();
                var flippedPixels = new Color[pixels.Length];
                for (int i = 0; i < _previewTexture.height; i++)
                {
                    Array.Copy(pixels, i * _previewTexture.width, flippedPixels, (_previewTexture.height - i - 1) * _previewTexture.width, _previewTexture.width);
                }
                _previewTexture.SetPixels(flippedPixels);
#else
                Graphics.CopyTexture(_renderTexture, _previewTexture);
#endif

                AsyncGPUReadback.RequestIntoNativeArray(ref _captureBuffer, _renderTexture, 0, _textureFormat, OnReadback);
            }
            catch (Exception e)
            {
                Utils.Error(e);
            }
            return textureChanged;
        }
    }
}

