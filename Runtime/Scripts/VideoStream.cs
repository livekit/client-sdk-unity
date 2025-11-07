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
		public enum ColorStandard
		{
			Auto = 0,
			BT601 = 1,
			BT709 = 2,
		}

        public delegate void FrameReceiveDelegate(VideoFrame frame);
        public delegate void TextureReceiveDelegate(Texture tex);
        public delegate void TextureUploadDelegate();

        internal readonly FfiHandle Handle;
        private VideoStreamInfo _info;
        private bool _disposed = false;
        private bool _dirty = false;
        private bool _useGpuYuvToRgb = true;
		private string _lastColorConversionPathLog;
		private bool _swapUV = false;
		private bool _fullRange = false;
		private ColorStandard _colorStandard = ColorStandard.BT709;
		private bool _invertU = false;
		private bool _invertV = false;
		private int _debugMode = 0;

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

		public bool SwapUV
		{
			get => _swapUV;
			set => _swapUV = value;
		}

		public bool FullRangeYuv
		{
			get => _fullRange;
			set => _fullRange = value;
		}

		public ColorStandard ColorStandardMode
		{
			get => _colorStandard;
			set => _colorStandard = value;
		}

		public bool InvertU
		{
			get => _invertU;
			set => _invertU = value;
		}

		public bool InvertV
		{
			get => _invertV;
			set => _invertV = value;
		}

		/// 0 = normal, 1 = Y, 2 = U, 3 = V
		public int DebugMode
		{
			get => _debugMode;
			set => _debugMode = value;
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

                var textureChanged = false;
                if (_convertRt == null || _convertRt.width != rWidth || _convertRt.height != rHeight)
                {
                    if (_convertRt != null)
                    {
                        _convertRt.Release();
                        UnityEngine.Object.Destroy(_convertRt);
                    }
                    _convertRt = new RenderTexture((int)rWidth, (int)rHeight, 0, RenderTextureFormat.ARGB32);
                    _convertRt.Create();
                    Texture = _convertRt;
                    textureChanged = true;
                }

                if (_useGpuYuvToRgb)
                {
                    if (_yuvToRgbMaterial == null)
                    {
                        var shader = Shader.Find("Hidden/LiveKit/YUV2RGB");
                        if (shader != null)
                            _yuvToRgbMaterial = new Material(shader);
                    }

                    // _convertRt ensured above

                    // Ensure YUV plane textures
                    if (_planeY == null || _planeY.width != rWidth || _planeY.height != rHeight)
                    {
                        if (_planeY != null) UnityEngine.Object.Destroy(_planeY);
						_planeY = new Texture2D((int)rWidth, (int)rHeight, TextureFormat.R8, false, true);
                        _planeY.filterMode = FilterMode.Bilinear;
                        _planeY.wrapMode = TextureWrapMode.Clamp;
                    }
                    var chromaW = (int)(rWidth / 2);
                    var chromaH = (int)(rHeight / 2);
                    if (_planeU == null || _planeU.width != chromaW || _planeU.height != chromaH)
                    {
                        if (_planeU != null) UnityEngine.Object.Destroy(_planeU);
						_planeU = new Texture2D(chromaW, chromaH, TextureFormat.R8, false, true);
						_planeU.filterMode = FilterMode.Point;
                        _planeU.wrapMode = TextureWrapMode.Clamp;
                    }
                    if (_planeV == null || _planeV.width != chromaW || _planeV.height != chromaH)
                    {
                        if (_planeV != null) UnityEngine.Object.Destroy(_planeV);
						_planeV = new Texture2D(chromaW, chromaH, TextureFormat.R8, false, true);
						_planeV.filterMode = FilterMode.Point;
                        _planeV.wrapMode = TextureWrapMode.Clamp;
                    }

                    // Upload planes (assuming NormalizeStride = true)
                    var info = VideoBuffer.Info;
                    if (info.Components.Count >= 3)
                    {
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

                    if (_yuvToRgbMaterial != null)
                    {
						// Select color matrix, preferring explicit override when set
						bool useBt709;
						if (_colorStandard == ColorStandard.BT601)
							useBt709 = false;
						else if (_colorStandard == ColorStandard.BT709)
							useBt709 = true;
						else
							useBt709 = (rWidth >= 1280) || (rHeight >= 720); // heuristic
						_yuvToRgbMaterial.SetFloat("_ColorStd", useBt709 ? 1.0f : 0.0f);
						_yuvToRgbMaterial.SetFloat("_SwapUV", _swapUV ? 1.0f : 0.0f);
						_yuvToRgbMaterial.SetFloat("_FullRange", _fullRange ? 1.0f : 0.0f);
						_yuvToRgbMaterial.SetFloat("_InvertU", _invertU ? 1.0f : 0.0f);
						_yuvToRgbMaterial.SetFloat("_InvertV", _invertV ? 1.0f : 0.0f);
						_yuvToRgbMaterial.SetFloat("_DebugMode", _debugMode);
						var path = useBt709 ? "GPU shader YUV->RGB (BT.709)" : "GPU shader YUV->RGB (BT.601)";
						if (_swapUV) path += " +SwapUV";
						if (_fullRange) path += " +FullRange";
						if (_invertU) path += " +InvertU";
						if (_invertV) path += " +InvertV";
						if (_debugMode != 0) path += $" +Debug({(DebugMode==1?"Y":DebugMode==2?"U":"V")})";
						LogConversionPath(path);
                        _yuvToRgbMaterial.SetTexture("_TexY", _planeY);
                        _yuvToRgbMaterial.SetTexture("_TexU", _planeU);
                        _yuvToRgbMaterial.SetTexture("_TexV", _planeV);
                        Graphics.Blit(Texture2D.blackTexture, _convertRt, _yuvToRgbMaterial);
                    }
                    else
                    {
						LogConversionPath("CPU conversion (shader not found)");
                        // Fallback to CPU conversion if shader not found
                        var rgba = VideoBuffer.ToRGBA();
                        var tempTex = new Texture2D((int)rWidth, (int)rHeight, TextureFormat.RGBA32, false);
                        tempTex.LoadRawTextureData((IntPtr)rgba.Info.DataPtr, (int)rgba.GetMemorySize());
                        tempTex.Apply();
                        Graphics.Blit(tempTex, _convertRt);
                        UnityEngine.Object.Destroy(tempTex);
                        rgba.Dispose();
                    }
                }
                else
                {
					LogConversionPath("CPU conversion (_useGpuYuvToRgb=false)");
                    var rgba = VideoBuffer.ToRGBA();
                    var tempTex = new Texture2D((int)rWidth, (int)rHeight, TextureFormat.RGBA32, false);
                    tempTex.LoadRawTextureData((IntPtr)rgba.Info.DataPtr, (int)rgba.GetMemorySize());
                    tempTex.Apply();
                    Graphics.Blit(tempTex, _convertRt);
                    UnityEngine.Object.Destroy(tempTex);
                    rgba.Dispose();
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