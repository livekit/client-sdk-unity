using System;
using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;
using System.Threading.Tasks;

namespace LiveKit
{
    public class TextureVideoSource : RtcVideoSource
    {
        public Texture Texture { get; }

        public override int GetWidth()
        {
            return Texture.width;
        }

        public override int GetHeight()
        {
            return Texture.height;
        }

        public TextureVideoSource(Texture texture, VideoBufferType bufferType = VideoBufferType.Rgba) : base(VideoStreamSource.Texture, bufferType)
        {
            Texture = texture;
            _data = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(bufferType), Allocator.Persistent);
        }


        ~TextureVideoSource()
        {
            Dispose(); 
        }

        // Read the texture data into a native array asynchronously
        protected override void ReadBuffer()
        {
            if (_reading)
                return;
            _reading = true;
            var gpuTextureFormat = GetTextureFormat(_bufferType); 
            if (!SystemInfo.IsFormatSupported(Texture.graphicsFormat, FormatUsage.ReadPixels))
            {
                if (_dest == null || _dest.width != GetWidth() || _dest.height != GetHeight())
                {

                    _data = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(_bufferType), Allocator.Persistent);
                    _dest = new Texture2D(GetWidth(), GetHeight(), gpuTextureFormat, false);
                }
                Graphics.CopyTexture(Texture, _dest);
            }
            else
            {
                _dest = Texture;
            }
            
            AsyncGPUReadback.RequestIntoNativeArray(ref _data, _dest, 0, gpuTextureFormat, OnReadback);
        }
    }
}

