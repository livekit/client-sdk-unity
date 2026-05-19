using LiveKit.Proto;
using Unity.Collections;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// Test-only <see cref="RtcVideoSource"/> that pushes a continuous stream of
    /// zero-filled RGBA frames at the resolution given to the constructor. Used
    /// by tests that need actual media flow (e.g. validating per-frame metadata
    /// round-trips through the FFI / RTP path).
    /// </summary>
    public sealed class MetadataTestVideoSource : RtcVideoSource
    {
        private readonly int _width;
        private readonly int _height;

        public override int GetWidth() => _width;
        public override int GetHeight() => _height;

        protected override VideoRotation GetVideoRotation() => VideoRotation._0;

        protected override bool ReadBuffer()
        {
            if (!_captureBuffer.IsCreated)
            {
                _captureBuffer = new NativeArray<byte>(
                    _width * _height * 4,
                    Allocator.Persistent,
                    NativeArrayOptions.ClearMemory);
            }
            _requestPending = true;
            return false;
        }

        public MetadataTestVideoSource(int width = 16, int height = 16)
            : base(VideoStreamSource.Texture, VideoBufferType.Rgba)
        {
            _width = width;
            _height = height;
            Init();
        }
    }
}
