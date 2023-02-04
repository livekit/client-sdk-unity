using System;
using System.Collections;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace LiveKit
{
    public sealed class VideoSink : IDisposable
    {
        public delegate void FrameReceiveDelegate(VideoFrame frame);
        public delegate void TextureReceiveDelegate(Texture2D tex2d);
        public delegate void TextureUploadDelegate();

        private VideoSinkInfo _info;
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
        public VideoFrameBuffer VideoBuffer;

        internal VideoSink(VideoSinkInfo info)
        {
            _info = info;
            FFIClient.Instance.TrackEventReceived += OnTrackEvent;
        }

        ~VideoSink()
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

        /// This function assumes the size of the buffer is valid
        internal bool UploadBuffer()
        {
            var texData = Texture.GetRawTextureData<byte>();

            /// TODO(theomonnom): Support other buffer types: NativeBuffer
            var i420 = VideoBuffer.ToI420();
            VideoBuffer = i420;

            unsafe
            {
                var texPtr = NativeArrayUnsafeUtility.GetUnsafePtr(texData);
                i420.ToARGB(VideoFormatType.FormatAbgr, (IntPtr)texPtr, Texture.width * 4, Texture.width, Texture.height);
            }

            Texture.Apply();
            return true;
        }

        public IEnumerator UpdateRoutine()
        {
            if (_disposed)
                yield break;

            if (VideoBuffer == null || !VideoBuffer.IsValid || !_dirty)
                goto Wait;

            var rWidth = VideoBuffer.Width;
            var rHeight = VideoBuffer.Height;

            var textureChanged = false;
            if (Texture == null || Texture.width != rWidth || Texture.height != rHeight)
            {
                // Recreate the texture
                Texture = new Texture2D(rWidth, rHeight, TextureFormat.RGBA32, mipChain: false, linear: true);
                textureChanged = true;
            }

            UploadBuffer();

            if (textureChanged)
                TextureReceived?.Invoke(Texture);

            TextureUploaded?.Invoke();
            _dirty = false;

        Wait:
            yield return new WaitForEndOfFrame();
        }

        /// Stop must be called on TrackUnsubscribed
        /// This function isn't called on Dispose because we must initialize correctly
        /// the FFIHandle to avoid memory leak (on each FrameReceiveEvent from the server)
        internal void Stop()
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

            VideoBuffer?.Dispose();
            VideoBuffer = buffer;
            _dirty = true;

            FrameReceived?.Invoke(frame);
        }
    }
}
