using UnityEngine;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine.Rendering;
using Unity.Collections;

namespace LiveKit
{
    public class CameraVideoSource : RtcVideoSource
    {
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

            var targetFormat = Utils.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
            _dest = new RenderTexture(GetWidth(), GetHeight(), 0, targetFormat);
            camera.targetTexture = _dest as RenderTexture;
            _data = new NativeArray<byte>(GetWidth() * GetHeight() * GetStrideForBuffer(bufferType), Allocator.Persistent);
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
        protected override void ReadBuffer()
        {
            if (_reading)
                return;
            _reading = true;
            AsyncGPUReadback.RequestIntoNativeArray(ref _data, _dest, 0, GetTextureFormat(_bufferType), OnReadback);
        }
    }
}

