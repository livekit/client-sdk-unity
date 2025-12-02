using System;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;
using LiveKit.Internal.FFIClients.Requests;
using System.Collections;

namespace LiveKit
{
    public class VideoStream
    {
        public delegate void FrameReceiveDelegate(VideoFrame frame);
        public delegate void TextureReceiveDelegate(Texture tex);
        public delegate void TextureUploadDelegate();

        internal readonly FfiHandle Handle;
        private VideoStreamInfo _info;
        private bool _disposed = false;
        private bool _dirty = false;
        private YuvToRgbConverter _converter;

        /// Called when we receive a new frame from the VideoTrack
        public event FrameReceiveDelegate FrameReceived;

        /// Called when we receive a new texture (first texture or the resolution changed)
        public event TextureReceiveDelegate TextureReceived;

        /// Called when we upload the texture to the GPU
        public event TextureUploadDelegate TextureUploaded;

        /// The texture changes every time the video resolution changes.
        /// Can be null if UpdateRoutine isn't started
        public RenderTexture Texture { private set; get; }
        public VideoFrameBuffer VideoBuffer { private set; get; }

        protected bool _playing = false;

        public VideoStream(IVideoTrack videoTrack)
        {
            if (!videoTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("videotrack's room is invalid");

            if (!videoTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("videotrack's participant is invalid");

            using var request = FFIBridge.Instance.NewRequest<NewVideoStreamRequest>();
            var newVideoStream = request.request;
            newVideoStream.TrackHandle = (ulong)videoTrack.TrackHandle.DangerousGetHandle();
            newVideoStream.Type = VideoStreamType.VideoStreamNative;
            newVideoStream.Format = VideoBufferType.I420;
            newVideoStream.NormalizeStride = true;
            using var response = request.Send();
            FfiResponse res = response;
            Handle = FfiHandle.FromOwnedHandle(res.NewVideoStream.Stream.Handle);
            FfiClient.Instance.VideoStreamEventReceived += OnVideoStreamEvent;
        }

        ~VideoStream()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    VideoBuffer?.Dispose();
                }
                // Unity objects must be destroyed on main thread
                _converter?.Dispose();
                _converter = null;
                // Texture is owned and cleaned up by _converter. Set to null to avoid holding a reference to a disposed RenderTexture.
                Texture = null;
                _disposed = true;
            }
        }

        public virtual void Start()
        {
            Stop();
            _playing = true;
        }

        public virtual void Stop()
        {
            _playing = false;
        }

        public IEnumerator Update()
        {
            while (_playing)
            {
                yield return null;

                if (_disposed)
                    break;

                if (VideoBuffer == null || !VideoBuffer.IsValid || !_dirty)
                    continue;

                _dirty = false;
                var rWidth = VideoBuffer.Width;
                var rHeight = VideoBuffer.Height;

                if (_converter == null) _converter = new YuvToRgbConverter();
                var textureChanged = _converter.EnsureOutput((int)rWidth, (int)rHeight);
                _converter.Convert(VideoBuffer);
                if (textureChanged) Texture = _converter.Output;

                if (textureChanged)
                    TextureReceived?.Invoke(Texture);

                TextureUploaded?.Invoke();
            }

            yield break;
        }

        // Handle new video stream events
        private void OnVideoStreamEvent(VideoStreamEvent e)
        {
            if (e.StreamHandle != (ulong)Handle.DangerousGetHandle())
                return;

            if (e.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived)
                return;
 
            var newBuffer = e.FrameReceived.Buffer;
            var handle = new FfiHandle((IntPtr)newBuffer.Handle.Id);
            var frameInfo = newBuffer.Info;

            var frame = new VideoFrame(frameInfo, e.FrameReceived.TimestampUs, e.FrameReceived.Rotation);
            var buffer = VideoFrameBuffer.Create(handle, frameInfo);

            VideoBuffer?.Dispose();
            VideoBuffer = buffer;
            _dirty = true;

            FrameReceived?.Invoke(frame);
        }
    }
}