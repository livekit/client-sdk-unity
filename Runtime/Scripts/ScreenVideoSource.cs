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
    public class ScreenVideoSource : RtcVideoSource
    {
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
            _data = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(bufferType), Allocator.Persistent);
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
        protected override void ReadBuffer()
        {
            if (_reading)
                return;
            _reading = true;
            if (_dest == null)
            {
                var targetFormat = Utils.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
                _dest = new RenderTexture(GetWidth(), GetHeight(), 0, targetFormat);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_dest as RenderTexture);
            AsyncGPUReadback.RequestIntoNativeArray(ref _data, _dest, 0, GetTextureFormat(_bufferType), OnReadback);
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

