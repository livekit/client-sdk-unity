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
        private readonly object _frameLock = new object();
        // Separates frame production from consumption:
        // - OnVideoStreamEvent produces the latest native frame coming from Rust
        // - Update() consumes at most one frame per Unity tick for upload/render
        //
        // Keeping the "next frame" in a dedicated slot lets Unity coalesce bursts down to the
        // newest pending frame without overwriting the frame currently being uploaded.
        private VideoFrameBuffer _pendingBuffer;

        /// Called when we receive a new frame from the VideoTrack
        public event FrameReceiveDelegate FrameReceived;

        /// Called when we receive a new texture (first texture or the resolution changed)
        public event TextureReceiveDelegate TextureReceived;

        /// Called when we upload the texture to the GPU
        public event TextureUploadDelegate TextureUploaded;

        /// The texture changes every time the video resolution changes.
        /// Can be null if UpdateRoutine isn't started
        public RenderTexture Texture { private set; get; }
        // The frame currently owned by the Unity update/render path. Update() swaps the latest
        // pending frame into this slot before converting/uploading it, so this represents the
        // frame being actively consumed rather than the next frame arriving from Rust.
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
            if (_disposed)
                return;

            // Remove the long-lived delegate reference first so this stream can be collected as
            // soon as user code drops it, and so late native callbacks cannot mutate disposed
            // frame/converter state.
            FfiClient.Instance.VideoStreamEventReceived -= OnVideoStreamEvent;

            lock (_frameLock)
            {
                // Native frame buffers are not Unity objects, so always release them even on the
                // finalizer path. This keeps the stream from leaking native frame handles if user
                // code forgets to call Dispose().
                VideoBuffer?.Dispose();
                VideoBuffer = null;
                _pendingBuffer?.Dispose();
                _pendingBuffer = null;
            }

            if (disposing)
            {
                // Unity objects must be destroyed on main thread, so only touch the converter and
                // RenderTexture when Dispose() is called explicitly by user code.
                _converter?.Dispose();
                _converter = null;
                // Texture is owned and cleaned up by _converter. Set to null to avoid holding a
                // reference to a disposed RenderTexture.
                Texture = null;
            }

            Handle.Dispose();
            _disposed = true;
        }

        public virtual void Start()
        {
            Stop();
            _playing = true;
        }

        public virtual void Stop()
        {
            _playing = false;

            // When the stream has no active consumer, do not keep the latest native frame alive.
            // Rust may still be producing frames depending on its queue configuration, but Unity
            // drops them immediately until Start() is called again.
            lock (_frameLock)
            {
                _pendingBuffer?.Dispose();
                _pendingBuffer = null;
                VideoBuffer?.Dispose();
                VideoBuffer = null;
                _dirty = false;
            }
        }

        public IEnumerator Update()
        {
            while (_playing)
            {
                yield return null;

                if (_disposed)
                    break;

                VideoFrameBuffer nextBuffer = null;
                lock (_frameLock)
                {
                    if (_dirty)
                    {
                        nextBuffer = _pendingBuffer;
                        _pendingBuffer = null;
                        _dirty = false;
                    }
                }

                if (nextBuffer == null)
                    continue;

                // Latest-frame-wins: if Rust buffered multiple frames, the intake path keeps only
                // the newest pending frame. Update() uploads at most one frame per Unity tick.
                VideoBuffer?.Dispose();
                VideoBuffer = nextBuffer;

                if (!VideoBuffer.IsValid)
                {
                    VideoBuffer.Dispose();
                    VideoBuffer = null;
                    continue;
                }

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
            if (_disposed)
                return;

            if (e.StreamHandle != (ulong)Handle.DangerousGetHandle())
                return;

            if (e.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var newBuffer = e.FrameReceived.Buffer;
            var handle = new FfiHandle((IntPtr)newBuffer.Handle.Id);
            var frameInfo = newBuffer.Info;
            // Create a managed wrapper around the native frame handle. This does not copy the
            // underlying video payload; the wrapper simply owns the FFI handle until the frame is
            // either uploaded or dropped.
            var buffer = VideoFrameBuffer.Create(handle, frameInfo);
            if (buffer == null)
            {
                handle.Dispose();
                return;
            }

            // If there is no active consumer, keep draining frames from Rust but drop them
            // immediately on the Unity side to avoid growing native memory or preserving stale
            // frames. The producer queue can be size 1, bounded N, or unbounded; this behavior is
            // correct for all three because Unity only wants the most recent renderable frame.
            if (!_playing)
            {
                buffer.Dispose();
                return;
            }

            lock (_frameLock)
            {
                if (_disposed || !_playing)
                {
                    buffer.Dispose();
                    return;
                }

                // Latest-frame-wins coalescing. If Rust delivers several frames before Update()
                // runs again, replace the pending frame with the newest one and drop the older
                // native buffer immediately.
                _pendingBuffer?.Dispose();
                _pendingBuffer = buffer;
                _dirty = true;
            }

            // Avoid allocating VideoFrame objects when nobody is observing them.
            if (FrameReceived != null)
            {
                var frame = new VideoFrame(frameInfo, e.FrameReceived.TimestampUs, e.FrameReceived.Rotation);
                FrameReceived.Invoke(frame);
            }
        }
    }
}
