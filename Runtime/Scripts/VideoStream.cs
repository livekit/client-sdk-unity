#if !UNITY_WEBGL

using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.Tracks;
using System;
using UnityEngine;

namespace LiveKit
{
    public sealed class VideoFrameEvent
    {
        private VideoFrame _frame;
        public VideoFrame Frame => _frame;
        private long _timestamp;
        public long Timestamp => _timestamp;
        private VideoRotation _rotation;
        public VideoRotation Rotation => _rotation;

        public VideoFrameEvent(VideoFrame frame, long timeStamp, VideoRotation rot)
        {
            _frame = frame;
            _timestamp = timeStamp;
            _rotation = rot;
        }

        public bool IsValid
        {
            get
            {
                if(_frame != null)
                {
                    return _frame.IsValid;
                }
                return false;
            }
        }

        public void Dispose()
        {
            _frame?.Dispose();
        }
    }

    [Obsolete]
    public class VideoStream
    {
        public delegate void FrameReceiveDelegate(VideoFrameEvent frameEvent);


        public delegate void TextureReceiveDelegate(Texture2D tex2d);


        public delegate void TextureUploadDelegate();


        internal FfiHandle Handle { get; private set; }

        private VideoStreamInfo _info;
        private bool _dirty = false;
        private bool _playing = false;
        private volatile bool disposed = false;

        /// Called when we receive a new frame from the VideoTrack
        public event FrameReceiveDelegate FrameReceived;

        /// Called when we receive a new texture (first texture or the resolution changed)
        public event TextureReceiveDelegate TextureReceived;

        /// Called when we upload the texture to the GPU
        public event TextureUploadDelegate TextureUploaded;

        /// The texture changes every time the video resolution changes.
        /// Can be null if UpdateRoutine isn't started
        public Texture2D Texture { private set; get; }
        public VideoFrameEvent? VideoBuffer { get; private set; }
        
        private readonly object _lock = new();

        public VideoStream(ITrack videoTrack, VideoBufferType format)
        {
            if (!videoTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("videotrack's room is invalid");

            if (!videoTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("videotrack's participant is invalid");
             
            using var request = FFIBridge.Instance.NewRequest<NewVideoStreamRequest>();
            var newVideoStream = request.request;
            newVideoStream.TrackHandle = (ulong)videoTrack.Handle.DangerousGetHandle();
            newVideoStream.Format = format;
            newVideoStream.NormalizeStride = true;
            newVideoStream.Type = VideoStreamType.VideoStreamNative;
            using var response = request.Send();
            FfiResponse res = response;
            var streamInfo = res.NewVideoStream.Stream;
            Handle = IFfiHandleFactory.Default.NewFfiHandle(streamInfo.Handle.Id);
            FfiClient.Instance.VideoStreamEventReceived += OnVideoStreamEvent;
        }

        ~VideoStream()
        {
            Dispose(false);
        }

        public void Start()
        {
            Stop();
            _playing = true;
        }

        public void Stop()
        {
            _playing = false;
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
            if (!disposed)
            {
                if (disposing)
                    VideoBuffer?.Dispose();
                disposed = true;
            }
        }

        // Needs to be on main thread
        public void Update()
        {
            if (!_playing || disposed) return;
            lock (_lock)
            {
                if (VideoBuffer == null || !VideoBuffer.IsValid || !_dirty)
                    return;

                _dirty = false;
                var rWidth = VideoBuffer.Frame.Width;
                var rHeight = VideoBuffer.Frame.Height;

                var textureChanged = false;
                if (Texture == null || Texture.width != rWidth || Texture.height != rHeight)
                {
                    if (Texture != null) UnityEngine.Object.Destroy(Texture);
                    Texture = new Texture2D((int)rWidth, (int)rHeight, TextureFormat.RGBA32, false);
                    Texture.ignoreMipmapLimit = false;
                    textureChanged = true;
                }

                int size = (int)VideoBuffer.Frame.GetMemorySize();
                unsafe
                {
                    Texture.LoadRawTextureData(VideoBuffer.Frame.Handle.DangerousGetHandle(), (int)size); 
                }
                Texture.Apply();

                if (textureChanged)
                    TextureReceived?.Invoke(Texture);

                TextureUploaded?.Invoke();
            }
        }

        private void OnVideoStreamEvent(VideoStreamEvent e)
        {
            if (e.StreamHandle != (ulong)Handle.DangerousGetHandle())
                return;

            if (e.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived)
                return;
              
            var bufferInfo = e.FrameReceived.Buffer.Info;

            var frame = VideoFrame.FromOwnedInfo(e.FrameReceived.Buffer);
            var evt = new VideoFrameEvent(frame, e.FrameReceived.TimestampUs, e.FrameReceived.Rotation);

            lock (_lock)
            {
                VideoBuffer?.Dispose();
                VideoBuffer = evt;
                _dirty = true;
            }

            FrameReceived?.Invoke(evt);
        }
    }
}

#endif
