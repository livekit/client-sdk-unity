using System;
using LiveKit.Internal;
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
            FFIClient.Instance.TrackEventReceived += OnTrackEvent;
        }

        /// Dispose must be called on TrackUnsubscribed
        internal void Dispose()
        {
            FFIClient.Instance.TrackEventReceived -= OnTrackEvent;
        }

        private void OnTrackEvent(TrackEvent e)
        {
            if (e.TrackSid != _info.TrackSid)
                return;

            if (e.MessageCase != TrackEvent.MessageOneofCase.FrameReceived)
                return;

            var frameInfo = e.FrameReceived.Frame;
            var bufferInfo = e.FrameReceived.FrameBuffer;
            var handle = new FFIHandle((IntPtr)bufferInfo.Handle.Id);

            var frame = new VideoFrame(frameInfo);
            var buffer = VideoFrameBuffer.Create(handle, bufferInfo);

            FrameReceived?.Invoke(frame, buffer);
        }
    }
}
