using UnityEngine;
using LiveKit.Proto;
using UnityEngine.Rendering;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;
using System;

namespace LiveKit
{
    public class TextureVideoSource : RtcVideoSource
    {
        TextureFormat _textureFormat;
        private RenderTexture _flippedRT;

        public Texture Texture { get; }

        public override int GetWidth()
        {
            return Texture.width;
        }

        public override int GetHeight()
        {
            return Texture.height;
        }

        protected override VideoRotation GetVideoRotation()
        {
            return VideoRotation._0;
        }

        public TextureVideoSource(Texture texture, VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Texture, bufferType)
        {
            Texture = texture;
            base.Init();
        }

        ~TextureVideoSource()
        {
            Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _flippedRT != null)
            {
                _flippedRT.Release();
                UnityEngine.Object.Destroy(_flippedRT);
                _flippedRT = null;
            }
            base.Dispose(disposing);
        }

        // Read the texture data into a native array asynchronously
        protected override bool ReadBuffer()
        {
            if (_reading)
                return false;
            _reading = true;
            var textureChanged = false;

            if (_previewTexture == null || _previewTexture.width != GetWidth() || _previewTexture.height != GetHeight()) {
                var compatibleFormat = SystemInfo.GetCompatibleFormat(Texture.graphicsFormat, FormatUsage.ReadPixels);
                _textureFormat = GraphicsFormatUtility.GetTextureFormat(compatibleFormat);
                _bufferType = GetVideoBufferType(_textureFormat);
                _captureBuffer = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(_bufferType), Allocator.Persistent);
                _previewTexture = new Texture2D(GetWidth(), GetHeight(), _textureFormat, false);
                if (_flippedRT != null)
                {
                    _flippedRT.Release();
                    UnityEngine.Object.Destroy(_flippedRT);
                }
                _flippedRT = new RenderTexture(GetWidth(), GetHeight(), 0, compatibleFormat);
                textureChanged = true;
            }
            // Vertically flip into an intermediate RT so the bytes AsyncGPUReadback produces are
            // top-down (WebRTC expects top-down; Unity render textures are bottom-up on macOS/Metal
            // and other GL-origin platforms).
            Graphics.Blit(Texture, _flippedRT, new Vector2(1f, -1f), new Vector2(0f, 1f));
            Graphics.CopyTexture(_flippedRT, _previewTexture);
            AsyncGPUReadback.RequestIntoNativeArray(ref _captureBuffer, _flippedRT, 0, _textureFormat, OnReadback);
            return textureChanged;
        }
    }
}

