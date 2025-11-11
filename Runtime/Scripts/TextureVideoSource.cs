using LiveKit.Proto;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LiveKit
{
    public class TextureVideoSource : RtcVideoSource
    {
        TextureFormat _textureFormat;

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

        // Read the texture data into a native array asynchronously
        protected override bool ReadBuffer()
        {
            if (_reading)
            {
                return false;
            }

            _reading = true;
            var textureChanged = false;

            if (_previewTexture == null || _previewTexture.width != GetWidth() || _previewTexture.height != GetHeight())
            {
                var compatibleFormat = SystemInfo.GetCompatibleFormat(Texture.graphicsFormat, FormatUsage.ReadPixels);
                _textureFormat = GraphicsFormatUtility.GetTextureFormat(compatibleFormat);
                _bufferType = GetVideoBufferType(_textureFormat);
                _captureBuffer = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(_bufferType), Allocator.Persistent);
                _previewTexture = new RenderTexture(GetWidth(), GetHeight(), 0, compatibleFormat);
                textureChanged = true;
            }

            Graphics.Blit(Texture, _previewTexture, new Vector2(1f, -1f), new Vector2(0f, 1f));
            AsyncGPUReadback.RequestIntoNativeArray(ref _captureBuffer, _previewTexture, 0, _textureFormat, OnReadback);
            return textureChanged;
        }
    }
}

