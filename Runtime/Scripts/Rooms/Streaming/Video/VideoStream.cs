
using Cysharp.Threading.Tasks;
using LiveKit.Internal;
using LiveKit.Proto;
using Unity.Profiling;
using UnityEngine;

namespace LiveKit.Rooms.VideoStreaming
{

#if !UNITY_WEBGL
    public class VideoStream : IVideoStream
    {
        private static readonly ProfilerMarker marker = new("LiveKit.VideoStream.DecodeLastFrame");
        
        private readonly TextureFormat textureFormat;
        private readonly FfiHandle handle;
        private readonly LiveKit.Proto.VideoStreamInfo info;

        private Texture2D? lastDecoded;
        private VideoLastFrame? lastFrame;
        private bool disposed;

        public VideoStream(OwnedVideoStream ownedVideoStream, TextureFormat textureFormat)
        {
            this.textureFormat = textureFormat;
            handle = IFfiHandleFactory.Default.NewFfiHandle(ownedVideoStream.Handle!.Id);
            info = ownedVideoStream.Info!;
            FfiClient.Instance.VideoStreamEventReceived += OnVideoStreamEvent;
        }

        /// <summary>
        /// Supposed to be disposed ONLY by VideoStreams
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            handle.Dispose();
            DisposeLastDecodedIfNeededAsync().Forget();
            FfiClient.Instance.VideoStreamEventReceived -= OnVideoStreamEvent;
        }

        private async UniTaskVoid DisposeLastDecodedIfNeededAsync()
        {
            await UniTask.SwitchToMainThread();
            if (lastDecoded != null) Object.Destroy(lastDecoded);
        }

        private void OnVideoStreamEvent(VideoStreamEvent e)
        {
            if (e.StreamHandle != (ulong)handle.DangerousGetHandle())
                return;

            if (e.MessageCase != VideoStreamEvent.MessageOneofCase.FrameReceived)
                return;

            var bufferInfo = e.FrameReceived!.Buffer!.Info!;
            var frameHandle = IFfiHandleFactory.Default.NewFfiHandle(e.FrameReceived.Buffer.Handle.Id);

            var evt = new VideoLastFrame(bufferInfo, frameHandle);

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

            using ProfilerMarker.AutoScope scope = marker.Auto();
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
#endif

    public readonly struct VideoStreamInfo
    {
    }
}
