using LiveKit.Proto;
using Unity.Collections;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// Test-only <see cref="RtcVideoSource"/> registered with FFI at a fixed
    /// resolution. Two modes via <paramref name="pushFrames"/>:
    /// <list type="bullet">
    /// <item><c>false</c> (default): never pushes frames. Use when the test only
    /// needs the publication to propagate via signaling (e.g.
    /// <see cref="RemoteTrackPublication"/> APIs that operate on metadata).</item>
    /// <item><c>true</c>: pushes a continuous stream of zero-filled RGBA frames.
    /// Use when the test needs actual media flow (e.g. validating per-frame
    /// metadata round-trips through the FFI / RTP path).</item>
    /// </list>
    /// </summary>
    public sealed class TestVideoSource : RtcVideoSource
    {
        private readonly int _width;
        private readonly int _height;
        private readonly bool _pushFrames;

        public override int GetWidth() => _width;
        public override int GetHeight() => _height;

        protected override VideoRotation GetVideoRotation() => VideoRotation._0;

        protected override bool ReadBuffer()
        {
            if (!_pushFrames) return false;

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

        public TestVideoSource(bool pushFrames = false, int width = 16, int height = 16)
            : base(VideoStreamSource.Texture, VideoBufferType.Rgba)
        {
            _width = width;
            _height = height;
            _pushFrames = pushFrames;
            Init();
        }
    }
}
