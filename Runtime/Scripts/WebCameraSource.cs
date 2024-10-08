using UnityEngine;
using LiveKit.Proto;
using UnityEngine.Rendering;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;
using System;
using System.Runtime.InteropServices;

namespace LiveKit
{
    // VideoSource for Unity WebCamTexture
    public class WebCameraSource : RtcVideoSource
    {
        TextureFormat _textureFormat;

        public WebCamTexture Texture { get; }

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
            return VideoRotation._180;
        }

        public WebCameraSource(WebCamTexture texture, VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Texture, bufferType)
        {
            Texture = texture;
            base.Init();
        }

        ~WebCameraSource()
        {
            Dispose();
        }

        // Read the texture data into a native array asynchronously
        protected override bool ReadBuffer()
        {
            if (_reading && !Texture.isPlaying)
                return false;
            _reading = true;
            var textureChanged = false;

            if (_dest == null || _dest.width != GetWidth() || _dest.height != GetHeight())
            {
                var compatibleFormat = SystemInfo.GetCompatibleFormat(Texture.graphicsFormat, FormatUsage.ReadPixels);
                _textureFormat = GraphicsFormatUtility.GetTextureFormat(compatibleFormat);
                _bufferType = GetVideoBufferType(_textureFormat);
                _data = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(_bufferType), Allocator.Persistent);
                _dest = new Texture2D(GetWidth(), GetHeight(), TextureFormat.BGRA32, false);
                textureChanged = true;
            }

            Color32[] pixels = new Color32[GetWidth() * GetHeight()];
            Texture.GetPixels32(pixels);
            var bytes = MemoryMarshal.Cast<Color32, byte>(pixels);
            _data.CopyFrom(bytes.ToArray());
            _requestPending = true;
            Graphics.CopyTexture(Texture, _dest);

            return textureChanged;
        }
    }
}

