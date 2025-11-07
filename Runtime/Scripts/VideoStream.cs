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
        private bool _useGpuYuvToRgb = true;
		private string _lastColorConversionPathLog;
		// Fixed baseline: BT.709 limited, no UV swap

        private Material _yuvToRgbMaterial;
        private Texture2D _planeY;
        private Texture2D _planeU;
        private Texture2D _planeV;
        private RenderTexture _convertRt;

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
                // Unity objects must be destroyed on main thread; RT is destroyed below
                if (_planeY != null) UnityEngine.Object.Destroy(_planeY);
                if (_planeU != null) UnityEngine.Object.Destroy(_planeU);
                if (_planeV != null) UnityEngine.Object.Destroy(_planeV);
                if (_convertRt != null)
                {
                    _convertRt.Release();
                    UnityEngine.Object.Destroy(_convertRt);
                }
                if (_yuvToRgbMaterial != null) UnityEngine.Object.Destroy(_yuvToRgbMaterial);
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

        private bool EnsureRenderTexture(int width, int height)
        {
            var textureChanged = false;
            if (_convertRt == null || _convertRt.width != width || _convertRt.height != height)
            {
                if (_convertRt != null)
                {
                    _convertRt.Release();
                    UnityEngine.Object.Destroy(_convertRt);
                }
                _convertRt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                _convertRt.Create();
                Texture = _convertRt;
                textureChanged = true;
            }
            return textureChanged;
        }

        private void EnsureGpuMaterial()
        {
            if (_yuvToRgbMaterial == null)
            {
                var shader = Shader.Find("Hidden/LiveKit/YUV2RGB");
                if (shader != null)
                    _yuvToRgbMaterial = new Material(shader);
            }
        }

        private static void EnsurePlaneTexture(ref Texture2D tex, int width, int height, TextureFormat format, FilterMode filterMode)
        {
            if (tex == null || tex.width != width || tex.height != height)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                tex = new Texture2D(width, height, format, false, true);
                tex.filterMode = filterMode;
                tex.wrapMode = TextureWrapMode.Clamp;
            }
        }

        private void EnsureYuvPlaneTextures(int width, int height)
        {
            EnsurePlaneTexture(ref _planeY, width, height, TextureFormat.R8, FilterMode.Bilinear);
            var chromaW = width / 2;
            var chromaH = height / 2;
            EnsurePlaneTexture(ref _planeU, chromaW, chromaH, TextureFormat.R8, FilterMode.Point);
            EnsurePlaneTexture(ref _planeV, chromaW, chromaH, TextureFormat.R8, FilterMode.Point);
        }

        private void UploadYuvPlanes()
        {
            var info = VideoBuffer.Info;
            if (info.Components.Count < 3) return;
            var yComp = info.Components[0];
            var uComp = info.Components[1];
            var vComp = info.Components[2];

            _planeY.LoadRawTextureData((IntPtr)yComp.DataPtr, (int)yComp.Size);
            _planeY.Apply(false, false);
            _planeU.LoadRawTextureData((IntPtr)uComp.DataPtr, (int)uComp.Size);
            _planeU.Apply(false, false);
            _planeV.LoadRawTextureData((IntPtr)vComp.DataPtr, (int)vComp.Size);
            _planeV.Apply(false, false);
        }

        private void CpuConvertToRenderTarget(int width, int height)
        {
            var rgba = VideoBuffer.ToRGBA();
            var tempTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tempTex.LoadRawTextureData((IntPtr)rgba.Info.DataPtr, (int)rgba.GetMemorySize());
            tempTex.Apply();
            Graphics.Blit(tempTex, _convertRt);
            UnityEngine.Object.Destroy(tempTex);
            rgba.Dispose();
        }

        private void GpuConvertToRenderTarget()
        {
            _yuvToRgbMaterial.SetTexture("_TexY", _planeY);
            _yuvToRgbMaterial.SetTexture("_TexU", _planeU);
            _yuvToRgbMaterial.SetTexture("_TexV", _planeV);
            Graphics.Blit(Texture2D.blackTexture, _convertRt, _yuvToRgbMaterial);
        }

		private void LogConversionPath(string path)
		{
			if (_lastColorConversionPathLog == path)
				return;
			_lastColorConversionPathLog = path;
			Debug.Log($"[LiveKit] VideoStream color conversion: {path}");
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

                var textureChanged = EnsureRenderTexture((int)rWidth, (int)rHeight);

                if (_useGpuYuvToRgb)
                {
                    EnsureGpuMaterial();
                    EnsureYuvPlaneTextures((int)rWidth, (int)rHeight);
                    UploadYuvPlanes();

                    if (_yuvToRgbMaterial != null)
                    {
                        LogConversionPath("GPU shader YUV->RGB (BT.709 limited)");
                        GpuConvertToRenderTarget();
                    }
                    else
                    {
                        LogConversionPath("CPU conversion (shader not found)");
                        CpuConvertToRenderTarget((int)rWidth, (int)rHeight);
                    }
                }
                else
                {
                    LogConversionPath("CPU conversion (_useGpuYuvToRgb=false)");
                    CpuConvertToRenderTarget((int)rWidth, (int)rHeight);
                }

                if (textureChanged)
                    TextureReceived?.Invoke(Texture);

                TextureUploaded?.Invoke();
            }

            yield break;
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