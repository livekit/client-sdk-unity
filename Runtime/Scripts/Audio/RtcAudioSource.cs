using System;
using System.Collections;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFI.Requests;
using System.Threading;

using LiveKit.Internal.FFI;
namespace LiveKit
{
    /// <summary>
    /// Defines the type of audio source, influencing processing behavior.
    /// </summary>
    public enum RtcAudioSourceType
    {
        AudioSourceCustom = 0,
        AudioSourceMicrophone = 1
    }

    /// <summary>
    /// Capture source for a local audio track.
    /// </summary>
    public abstract class RtcAudioSource : IRtcSource, IDisposable
    {
        private static int nextDebugId = 0;

        /// <summary>
        /// Event triggered when audio samples are captured from the underlying source.
        /// Provides the audio data, channel count, and sample rate.
        /// </summary>
        /// <remarks>
        /// This event is not guaranteed to be called on the main thread. It must not be invoked
        /// concurrently: the source converts each block into one reusable buffer.
        /// </remarks>
        public abstract event Action<float[], int, int> AudioRead;

        private readonly RtcAudioSourceType _sourceType;
        public RtcAudioSourceType SourceType => _sourceType;

        /// <summary>
        /// Whether this source runs libwebrtc's audio processing over its capture. False when it was
        /// created without <see cref="AudioProcessingOptions"/>, or when the module could not be
        /// created and the source fell back to unprocessed capture.
        /// </summary>
        public bool AudioProcessingEnabled => _processor != null;
        private readonly int _debugId = Interlocked.Increment(ref nextDebugId);
        internal readonly uint _expectedSampleRate;
        internal readonly uint _expectedChannels;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;
        private readonly AudioProcessor _processor;

        // CaptureAudioFrame copies the PCM into its own buffer on the calling thread before the
        // request returns (livekit-ffi capture_frame, rust-sdks #289), so the pointer only has to
        // stay valid for the duration of the synchronous Send(). One reusable buffer, pinned around
        // the call, is enough. Audio thread only; see the AudioRead contract.
        private short[] _captureBuffer = Array.Empty<short>();

        // Cached so a capture registers its callback without allocating per frame.
        private readonly Action<CaptureAudioFrameCallback> _onCaptureCallback;
        private readonly Action _onCaptureCanceled;

        private volatile bool _muted = false;
        public override bool Muted => _muted;

        private bool _started = false;
        private volatile bool _disposed = false;
        private int _audioReadCount = 0;
        private int _sentFrameCount = 0;

        // Device-capture sources (microphone, AudioSource taps) don't know their format ahead of
        // time — it is whatever Unity's audio graph delivers. They use this constructor, which
        // configures the native source from Unity's current output configuration.
        protected RtcAudioSource(RtcAudioSourceType audioSourceType)
            : this(audioSourceType, 0, 0, null) { }

        /// <summary>
        /// Device-capture source whose audio is run through libwebrtc's audio processing (echo
        /// cancellation, noise suppression, gain control, high-pass filter) before it reaches the
        /// track. See <see cref="AudioProcessingOptions"/>. If the module cannot be created the
        /// source logs a warning and captures unprocessed.
        /// </summary>
        protected RtcAudioSource(RtcAudioSourceType audioSourceType, AudioProcessingOptions processing)
            : this(audioSourceType, 0, 0, processing) { }

        // Sources that generate a fixed, known format (e.g. test signal generators) declare it
        // directly. Passing 0 for either value falls back to the device configuration.
        protected RtcAudioSource(RtcAudioSourceType audioSourceType, uint sampleRate, uint channels)
            : this(audioSourceType, sampleRate, channels, null) { }

        protected RtcAudioSource(RtcAudioSourceType audioSourceType, uint sampleRate, uint channels, AudioProcessingOptions? processing)
        {
            _sourceType = audioSourceType;
            _onCaptureCallback = OnCaptureCallback;
            _onCaptureCanceled = OnCaptureCanceled;

            if (sampleRate > 0 && channels > 0)
            {
                _expectedSampleRate = sampleRate;
                _expectedChannels = channels;
            }
            else
            {
                (_expectedSampleRate, _expectedChannels) = ResolveDeviceFormat();
            }

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = _expectedChannels;
            newAudioSource.SampleRate = _expectedSampleRate;

            newAudioSource.Options = request.TempResource<AudioSourceOptions>();
            newAudioSource.Options.EchoCancellation = true;
            newAudioSource.Options.AutoGainControl = true;
            newAudioSource.Options.NoiseSuppression = true;
            using var response = request.Send();
            FfiResponse res = response;
            _info = res.NewAudioSource.Source.Info;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioSource.Source.Handle);
            Utils.Debug($"{DebugTag} created handle={Handle.DangerousGetHandle()} expectedRate={_expectedSampleRate} expectedChannels={_expectedChannels} sourceType={_sourceType}");

            if (processing is { } options && options.AnyProcessingEnabled)
            {
                try
                {
                    _processor = new AudioProcessor(options, SendProcessedFrame);
                }
                catch (Exception e)
                {
                    // Publish unprocessed rather than not at all.
                    Utils.Warning($"{DebugTag} audio processing unavailable, capturing unprocessed: {e.Message}");
                }
            }
        }

        // Format used when Unity reports no usable output configuration. Matches the FFI defaults.
        private const uint FallbackSampleRate = 48000;
        private const uint FallbackChannels = 1;

        // Reads Unity's actual output audio configuration. The capture path delivers buffers at the
        // DSP output rate/channel count (see AudioProbe), so this is the format the native source
        // must match. When the Unity audio system is disabled (Project Settings > Audio > Disable
        // Unity Audio, dedicated servers) Unity reports a 0 Hz rate and the Raw speaker mode. The
        // native source must not be created with that format: its 10 ms silence timer divides by
        // the channel count once the track is published. Fall back to the FFI defaults and warn;
        // capture through Unity audio cannot work in that state, but the process stays alive.
        private (uint sampleRate, uint channels) ResolveDeviceFormat()
        {
            var config = UnityEngine.AudioSettings.GetConfiguration();
            var sampleRate = config.sampleRate;
            var channels = SpeakerModeChannels(config.speakerMode);

            if (sampleRate <= 0 || channels == 0)
            {
                Utils.Warning($"{DebugTag} Unity reports no usable output format (sampleRate={sampleRate}, " +
                              $"speakerMode={config.speakerMode}); the Unity audio system is probably disabled. " +
                              $"Falling back to {FallbackSampleRate} Hz, {FallbackChannels} channel(s).");
                return (FallbackSampleRate, FallbackChannels);
            }

            Utils.Info($"Configured native audio source with sampleRate {sampleRate} and channels {channels}");

            return ((uint)sampleRate, channels);
        }

        private static uint SpeakerModeChannels(UnityEngine.AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case UnityEngine.AudioSpeakerMode.Mono: return 1;
                case UnityEngine.AudioSpeakerMode.Stereo: return 2;
                case UnityEngine.AudioSpeakerMode.Quad: return 4;
                case UnityEngine.AudioSpeakerMode.Surround: return 5;
                case UnityEngine.AudioSpeakerMode.Mode5point1: return 6;
                case UnityEngine.AudioSpeakerMode.Mode7point1: return 8;
                case UnityEngine.AudioSpeakerMode.Prologic: return 2;
                default: return 0;
            }
        }

        /// <summary>
        /// Begin capturing audio samples from the underlying source.
        /// </summary>
        public virtual void Start()
        {
            if (_started) return;
            AudioRead += OnAudioRead;
            _processor?.Start();
            _started = true;
            Utils.Debug($"{DebugTag} start");
        }

        /// <summary>
        /// Stop capturing audio samples from the underlying source.
        /// </summary>
        public virtual void Stop()
        {
            if (!_started) return;
            AudioRead -= OnAudioRead;
            _processor?.Stop();
            _started = false;
            Utils.Debug($"{DebugTag} stop");
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (_disposed) return;
            // A muted block still runs through the processing stage so the echo canceller keeps
            // seeing the near end next to its reference; SendProcessedFrame drops the output.
            // Without a processing stage there is nothing to keep warm.
            if (_muted && _processor == null) return;

            var readIndex = Interlocked.Increment(ref _audioReadCount);
            if (channels <= 0)
            {
                Utils.Warning($"{DebugTag} dropping audio frame #{readIndex} because channels={channels}");
                return;
            }

            if (data.Length == 0 || data.Length % channels != 0)
            {
                Utils.Warning($"{DebugTag} audio frame #{readIndex} has invalid shape samples={data.Length} channels={channels}");
                return;
            }

            if ((uint)sampleRate != _expectedSampleRate || (uint)channels != _expectedChannels)
            {
                Utils.Warning($"{DebugTag} audio frame #{readIndex} metadata mismatch actualRate={sampleRate} actualChannels={channels} expectedRate={_expectedSampleRate} expectedChannels={_expectedChannels} sourceType={_sourceType}");
            }

            // Optional processing stage: the block is re-chunked into 10 ms frames, run through the
            // module and delivered to SendFrame one chunk at a time via SendProcessedFrame.
            if (_processor != null && _processor.TryProcessCapture(data, channels, sampleRate))
                return;

            if (_muted) return;

            if (_captureBuffer.Length < data.Length)
                _captureBuffer = new short[data.Length];
            for (int i = 0; i < data.Length; i++)
                _captureBuffer[i] = PcmConvert.FloatToS16(data[i]);

            SendFrame(new ReadOnlySpan<short>(_captureBuffer, 0, data.Length), channels, sampleRate);
        }

        // Audio thread, from the processing stage. Muted output is dropped here, after the module
        // has seen the block.
        private void SendProcessedFrame(ReadOnlySpan<short> frame, int channels, int sampleRate)
        {
            if (_disposed || _muted) return;
            SendFrame(frame, channels, sampleRate);
        }

        // Hands one int16 frame to the native source. The frame is borrowed for the duration of
        // the call only; the native side has its own copy when Send() returns.
        private unsafe void SendFrame(ReadOnlySpan<short> frame, int channels, int sampleRate)
        {
            var frameIndex = Interlocked.Increment(ref _sentFrameCount);
            if (frameIndex <= 3 || frameIndex % 100 == 0)
            {
                Utils.Debug($"{DebugTag} capture frame #{frameIndex} samples={frame.Length} channels={channels} sampleRate={sampleRate} thread={Thread.CurrentThread.ManagedThreadId}");
            }

            using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();

            var pushFrame = request.request;
            pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();
            pushFrame.Buffer = audioFrameBufferInfo;
            pushFrame.Buffer.NumChannels = (uint)channels;
            pushFrame.Buffer.SampleRate = (uint)sampleRate;
            pushFrame.Buffer.SamplesPerChannel = (uint)(frame.Length / channels);

            // The callback only reports the outcome. It is registered before Send() so Rust cannot
            // complete a request Unity has nowhere to store; Send() cancels it if the call throws.
            FfiClient.Instance.RegisterPendingCallback(request.RequestAsyncId, static e => e.CaptureAudioFrame, _onCaptureCallback, _onCaptureCanceled);

            fixed (short* pcm = frame)
            {
                pushFrame.Buffer.DataPtr = (ulong)pcm;
                using var response = request.Send();
            }
        }

        // Main thread, posted by the FFI client.
        private void OnCaptureCallback(CaptureAudioFrameCallback callback)
        {
            if (callback.HasError)
                Utils.Error($"{DebugTag} audio capture failed asyncId={callback.AsyncId}: {callback.Error}");
        }

        // The FFI client dropped the pending callback (dispose, resume window, or a failed send).
        // Debug level: at quit the client sweeps every frame whose callback Rust never sent, one
        // line per frame, and a failed send is already logged as an error by the client.
        private void OnCaptureCanceled()
        {
            Utils.Debug($"{DebugTag} capture callback canceled");
        }

        /// <summary>
        /// Clears the audio processing stage's buffers. Call after the capture path restarts (e.g. a
        /// microphone resume) so stale samples do not misalign the echo canceller. No-op without
        /// processing.
        /// </summary>
        protected void ResetAudioProcessing() => _processor?.RequestReset();

        /// <summary>
        /// Mutes or unmutes the audio source.
        /// </summary>
        public override void SetMute(bool muted)
        {
            _muted = muted;
        }

        /// <summary>
        /// Disposes of the audio source, stopping it first if necessary.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing) Stop();

            _processor?.Dispose(disposing);
            Handle?.Dispose();
            _disposed = true;
            Utils.Debug($"{DebugTag} disposed");
        }

        ~RtcAudioSource()
        {
            Dispose(false);
        }

        [Obsolete("No longer used, audio sources should perform any preparation in Start() asynchronously")]
        public virtual IEnumerator Prepare(float timeout = 0) { yield break; }

        [Obsolete("Use Start() instead")]
        public IEnumerator PrepareAndStart()
        {
            Start();
            yield break;
        }

        private string DebugTag => $"RtcAudioSource#{_debugId}";
    }
}
