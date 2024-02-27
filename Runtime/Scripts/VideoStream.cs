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
  
            newVideoStream.TrackHandle = videoTrack.TrackHandle;
            newVideoStream.Type = VideoStreamType.VideoStreamNative;

            var request = new FfiRequest();
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
                   if (Texture != null) UnityEngine.Object.Destroy(Texture);
                    Texture = new Texture2D((int)rWidth, (int)rHeight, TextureFormat.RGBA32, false);
                    Texture.ignoreMipmapLimit = false;
                    textureChanged = true;
                }
                var rgba = VideoBuffer.ToRGBA();
                {
                    Texture.LoadRawTextureData((IntPtr)rgba.Info.DataPtr, (int)rgba.GetMemorySize()); 
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