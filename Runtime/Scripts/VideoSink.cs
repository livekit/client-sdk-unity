using LiveKit.Proto;

namespace LiveKit
{
    public sealed class VideoSink
    {
        public delegate void FrameReceiveDelegate(VideoFrame frame, VideoFrameBuffer buffer);

        private VideoSinkInfo _info;

        public event FrameReceiveDelegate FrameReceived;

        internal VideoSink(VideoSinkInfo info)
        {
            _info = info;
        }
    }
}
