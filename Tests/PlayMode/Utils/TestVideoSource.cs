using LiveKit.Proto;
using Unity.Collections;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// A programmatic video source for testing. Generates solid white or black
    /// RGBA frames without requiring GPU textures.
    /// </summary>
    public class TestVideoSource : RtcVideoSource
    {
        private readonly int _width;
        private readonly int _height;
        private bool _bright = false;

        public TestVideoSource(int width = 64, int height = 64)
            : base(VideoStreamSource.Texture, VideoBufferType.Rgba)
        {
            _width = width;
            _height = height;
            base.Init();
        }

        public override int GetWidth() => _width;
        public override int GetHeight() => _height;
        protected override VideoRotation GetVideoRotation() => VideoRotation._0;

        /// <summary>
        /// Set whether the frame is bright (white) or dark (black).
        /// </summary>
        public void SetBright(bool bright)
        {
            _bright = bright;
        }

        protected override bool ReadBuffer()
        {
            int size = _width * _height * 4;
            if (!_captureBuffer.IsCreated || _captureBuffer.Length != size)
            {
                if (_captureBuffer.IsCreated) _captureBuffer.Dispose();
                _captureBuffer = new NativeArray<byte>(size, Allocator.Persistent);
            }

            byte value = _bright ? (byte)255 : (byte)0;

            for (int i = 0; i < size; i += 4)
            {
                _captureBuffer[i]     = value; // R
                _captureBuffer[i + 1] = value; // G
                _captureBuffer[i + 2] = value; // B
                _captureBuffer[i + 3] = 255;   // A
            }

            _requestPending = true;
            return false;
        }

        ~TestVideoSource()
        {
            Dispose(false);
        }
    }
}
