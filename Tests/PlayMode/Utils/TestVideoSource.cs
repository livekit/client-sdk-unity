using LiveKit.Proto;
using Unity.Collections;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// A programmatic video source for testing. Encodes a pulse index as a
    /// spatial binary pattern: the frame is divided into 4 vertical strips,
    /// each either black or white, representing 4 bits of the index.
    /// This survives WebRTC lossy compression because detection only needs
    /// to distinguish black vs white in large uniform regions.
    /// </summary>
    public class TestVideoSource : RtcVideoSource
    {
        private readonly int _width;
        private readonly int _height;
        private int _pulseIndex = -1;

        /// <summary>
        /// Number of vertical strips used for binary encoding.
        /// 4 strips = 4 bits = values 0–15.
        /// </summary>
        public const int NumStrips = 4;

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
        /// Set the pulse index to encode. -1 = all black (between pulses).
        /// Index is encoded as 4-bit binary across vertical strips.
        /// </summary>
        public void SetPulseIndex(int index)
        {
            _pulseIndex = index;
        }

        protected override bool ReadBuffer()
        {
            int size = _width * _height * 4;
            if (!_captureBuffer.IsCreated || _captureBuffer.Length != size)
            {
                if (_captureBuffer.IsCreated) _captureBuffer.Dispose();
                _captureBuffer = new NativeArray<byte>(size, Allocator.Persistent);
            }

            int stripWidth = _width / NumStrips;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int strip = x / stripWidth;
                    bool bright = _pulseIndex >= 0 && ((_pulseIndex >> strip) & 1) == 1;
                    byte value = bright ? (byte)255 : (byte)0;

                    int offset = (y * _width + x) * 4;
                    _captureBuffer[offset]     = value; // R
                    _captureBuffer[offset + 1] = value; // G
                    _captureBuffer[offset + 2] = value; // B
                    _captureBuffer[offset + 3] = 255;   // A
                }
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
