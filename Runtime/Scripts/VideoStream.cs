using System;
using System.Collections;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace LiveKit
{
    public class VideoStream
    {
        public delegate void FrameReceiveDelegate(VideoFrame frame);
        public delegate void TextureReceiveDelegate(Texture2D tex2d);
        public delegate void TextureUploadDelegate();

        internal readonly FfiHandle Handle;
        private VideoStreamInfo _info;
        private bool _disposed = false;
        private bool _dirty = false;

        /// Called when we receive a new frame from the VideoTrack
        public event FrameReceiveDelegate FrameReceived;

        /// Called when we receive a new texture (first texture or the resolution changed)
        public event TextureReceiveDelegate TextureReceived;

        /// Called when we upload the texture to the GPU
        public event TextureUploadDelegate TextureUploaded;

        /// The texture changes every time the video resolution changes.
        /// Can be null if UpdateRoutine isn't started
        public Texture2D Texture { private set; get; }
        public VideoFrameBuffer VideoBuffer { private set; get; }

        public VideoStream(IVideoTrack videoTrack)
        {
            if (!videoTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("videotrack's room is invalid");

            if (!videoTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("videotrack's participant is invalid");

            var newVideoStream = new NewVideoStreamRequest();
            newVideoStream.RoomHandle = new FFIHandleId { Id = (ulong)room.Handle.DangerousGetHandle() };
            newVideoStream.ParticipantSid = participant.Sid;
            newVideoStream.TrackSid = videoTrack.Sid;
            newVideoStream.Type = VideoStreamType.VideoStreamNative;

            var request = new FFIRequest();
            request.NewVideoStream = newVideoStream;

            var resp = FfiClient.SendRequest(request);
            var streamInfo = resp.NewVideoStream.Stream;

            Handle = new FfiHandle((IntPtr)streamInfo.Handle.Id);
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
                    VideoBuffer?.Dispose();

                _disposed = true;
            }
        }

        internal bool UploadBuffer()
        {
            var data = Texture.GetRawTextureData<byte>();
            VideoBuffer = VideoBuffer.ToI420(); // TODO(theomonnom): Support other buffer types

            unsafe
            {
                var texPtr = NativeArrayUnsafeUtility.GetUnsafePtr(data);
                VideoBuffer.ToARGB(VideoFormatType.FormatAbgr, (IntPtr)texPtr, (uint)Texture.width * 4, (uint)Texture.width, (uint)Texture.height);
            }

            Texture.Apply();
            return true;
        }

        public IEnumerator Update()
        {
            while (true)
            {
                yield return null;

                if (_disposed)
                    break;

                if (VideoBuffer == null || !VideoBuffer.IsValid || !_dirty)
                    continue;

                _dirty = false;
                var rWidth = VideoBuffer.Width;
                var rHeight = VideoBuffer.Height;

                var textureChanged = false;
                if (Texture == null || Texture.width != rWidth || Texture.height != rHeight)
                {
                    Texture = new Texture2D((int)rWidth, (int)rHeight, TextureFormat.RGBA32, true, true);
                    textureChanged = true;
                }

                UploadBuffer();

                if (textureChanged)
                    TextureReceived?.Invoke(Texture);

                TextureUploaded?.Invoke();
            }
        }

        private void OnVideoStreamEvent(VideoStreamEvent e)
        {
            if (e.Handle.Id != (ulong)Handle.DangerousGetHandle())
                return;

            if (e.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var frameInfo = e.FrameReceived.Frame;
            var bufferInfo = e.FrameReceived.Buffer;
            var handle = new FfiHandle((IntPtr)bufferInfo.Handle.Id);

            var frame = new VideoFrame(frameInfo);
            var buffer = VideoFrameBuffer.Create(handle, bufferInfo);

            VideoBuffer?.Dispose();
            VideoBuffer = buffer;
            _dirty = true;

            FrameReceived?.Invoke(frame);
        }
    }
}