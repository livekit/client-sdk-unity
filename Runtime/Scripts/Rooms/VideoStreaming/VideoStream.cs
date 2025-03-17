using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;

namespace LiveKit.Rooms.VideoStreaming
{
    public class VideoStream : IVideoStream
    {
        private readonly IVideoStreams videoStreams;
        private readonly TextureFormat textureFormat;
        private readonly FfiHandle handle;
        private readonly VideoStreamInfo info;

        private Texture2D? lastDecoded;
        private VideoLastFrame? lastFrame;
        private bool disposed;

        public VideoStream(IVideoStreams videoStreams, OwnedVideoStream ownedVideoStream, TextureFormat textureFormat)
        {
            this.videoStreams = videoStreams;
            this.textureFormat = textureFormat;
            handle = IFfiHandleFactory.Default.NewFfiHandle(ownedVideoStream.Handle!.Id);
            info = ownedVideoStream.Info!;
            FfiClient.Instance.VideoStreamEventReceived += OnVideoStreamEvent;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            handle.Dispose();
            if (lastDecoded != null) Object.Destroy(lastDecoded);
            FfiClient.Instance.VideoStreamEventReceived -= OnVideoStreamEvent;
            videoStreams.Release(this);
        }

        private void OnVideoStreamEvent(VideoStreamEvent e)
        {
            if (e.StreamHandle != (ulong)handle.DangerousGetHandle())
                return;

            if (e.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var bufferInfo = e.FrameReceived!.Buffer!.Info!;

            var evt = new VideoLastFrame(bufferInfo);

            lock (this)
            {
                lastFrame?.Dispose();
                lastFrame = evt;
            }
        }

        public Texture2D? DecodeLastFrame()
        {
            if (disposed)
                return null;

            lock (this)
            {
                if (lastFrame.HasValue == false)
                    return lastDecoded;

                var frame = lastFrame.Value;

                var rWidth = frame.Width;
                var rHeight = frame.Height;

                if (lastDecoded == null || lastDecoded.width != rWidth || lastDecoded.height != rHeight)
                {
                    //TODO pooling
                    if (lastDecoded != null) Object.Destroy(lastDecoded);
                    lastDecoded = new Texture2D((int)rWidth, (int)rHeight, textureFormat, false);
                    lastDecoded.ignoreMipmapLimit = false;
                }

                int size = frame.MemorySize;
                lastDecoded.LoadRawTextureData(frame.Data, size);
                lastDecoded.Apply();

                frame.Dispose();
                lastFrame = null;

                return lastDecoded;
            }
        }
    }
}