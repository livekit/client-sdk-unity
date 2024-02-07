using System;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Rooms;
using LiveKit.Rooms.Tracks;

namespace LiveKit
{
    public class VideoStream
    {
        public delegate void FrameReceiveDelegate(VideoFrame frame);


        public delegate void TextureReceiveDelegate(Texture2D tex2d);


        public delegate void TextureUploadDelegate();


        internal FfiHandle Handle { get; private set; }

        private VideoStreamInfo _info;
        private bool _dirty = false;
        private volatile bool disposed = false;

        // Thread for parsing textures
        private Thread? frameThread;

        /// Called when we receive a new frame from the VideoTrack
        public event FrameReceiveDelegate FrameReceived;

        /// Called when we receive a new texture (first texture or the resolution changed)
        public event TextureReceiveDelegate TextureReceived;

        /// Called when we upload the texture to the GPU
        public event TextureUploadDelegate TextureUploaded;

        /// The texture changes every time the video resolution changes.
        /// Can be null if UpdateRoutine isn't started
        public Texture2D Texture { private set; get; }
        public VideoFrameBuffer? VideoBuffer { get; private set; }
        
        private readonly object _lock = new();

        public VideoStream(ITrack videoTrack)
        {
            if (videoTrack.Kind is not TrackKind.KindVideo)
                throw new InvalidOperationException("videoTrack is not a video track");
            
            if (!videoTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("videotrack's room is invalid");

            if (!videoTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("videotrack's participant is invalid");

            using var request = FFIBridge.Instance.NewRequest<NewVideoStreamRequest>();
            var newVideoStream = request.request;
            newVideoStream.TrackHandle = (ulong)room.Handle.DangerousGetHandle();
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

        public void StartStreaming()
        {
            StopStreaming();
            frameThread = new Thread(GetFrame);
            frameThread.Start();
        }

        public void StopStreaming()
        {
            frameThread?.Abort();
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                    VideoBuffer?.Dispose();

                disposed = true;
            }
        }

        private void UploadBuffer()
        {
            var data = Texture.GetRawTextureData<byte>();
            VideoBuffer = VideoBuffer.ToI420(); // TODO MindTrust-VID
            unsafe
            {
                var texPtr = NativeArrayUnsafeUtility.GetUnsafePtr(data);
                VideoBuffer.ToARGB(VideoFormatType.FormatAbgr, (IntPtr)texPtr, (uint)Texture.width * 4, (uint)Texture.width,
                    (uint)Texture.height);
            }

            Texture.Apply();
        }

        private void GetFrame()
        {
            while (!disposed)
            {
                Thread.Sleep(Constants.TASK_DELAY);

                lock (_lock)
                {
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
        }

        private void OnVideoStreamEvent(VideoStreamEvent e)
        {
            if (e.StreamHandle != (ulong)Handle.DangerousGetHandle())
                return;

            if (e.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var frameInfo = e.FrameReceived.Frame;
            var bufferInfo = e.FrameReceived.Buffer.Info;
            var handle = IFfiHandleFactory.Default.NewFfiHandle(e.FrameReceived.Buffer.Handle.Id);

            var frame = new VideoFrame(frameInfo);
            var buffer = VideoFrameBuffer.Create(handle, bufferInfo);

            lock (_lock)
            {
                VideoBuffer?.Dispose();
                VideoBuffer = buffer;
                _dirty = true;
            }

            FrameReceived?.Invoke(frame);
        }
    }
}