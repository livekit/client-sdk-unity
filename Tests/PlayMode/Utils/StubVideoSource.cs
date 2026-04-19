using LiveKit.Proto;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// Test-only <see cref="RtcVideoSource"/> that registers with FFI but never
    /// pushes frames. Used for tests that only require the video publication
    /// to propagate to subscribers via signaling (e.g. RemoteTrackPublication
    /// APIs that operate on metadata) without actual media flow.
    /// </summary>
    public sealed class StubVideoSource : RtcVideoSource
    {
        private readonly int _width;
        private readonly int _height;

        public override int GetWidth() => _width;
        public override int GetHeight() => _height;

        protected override VideoRotation GetVideoRotation() => VideoRotation._0;

        protected override bool ReadBuffer() => false;

        public StubVideoSource(int width = 16, int height = 16)
            : base(VideoStreamSource.Texture, VideoBufferType.Rgba)
        {
            _width = width;
            _height = height;
            Init();
        }
    }
}
