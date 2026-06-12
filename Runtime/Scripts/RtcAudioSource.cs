using System;
using System.Collections;
using System.Collections.Generic;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Diagnostics;
using System.Threading;

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
        private sealed class PendingAudioFrame
        {
            public NativeArray<short> FrameData;
            public int FrameIndex;
            public int SampleRate;
            public int Channels;
            public int SampleCount;
            public long StartedTimestamp;
        }

        private static int nextDebugId = 0;

        /// <summary>
        /// Event triggered when audio samples are captured from the underlying source.
        /// Provides the audio data, channel count, and sample rate.
        /// </summary>
        /// <remarks>
        /// This event is not guaranteed to be called on the main thread.
        /// </remarks>
        public abstract event Action<float[], int, int> AudioRead;

#if UNITY_IOS && !UNITY_EDITOR
        // iOS microphone sample rate is 24k
        public static uint DefaultMicrophoneSampleRate = 24000;

        public static uint DefaultSampleRate = 48000;
#else
        public static uint DefaultSampleRate = 48000;
        public static uint DefaultMicrophoneSampleRate = DefaultSampleRate;
#endif
        public static uint DefaultChannels = 2;

        private readonly RtcAudioSourceType _sourceType;
        public RtcAudioSourceType SourceType => _sourceType;
        private readonly int _debugId = Interlocked.Increment(ref nextDebugId);
        private readonly uint _expectedSampleRate;
        private readonly uint _expectedChannels;

        internal readonly FfiHandle Handle;
        protected AudioSourceInfo _info;

        // CaptureAudioFrame is asynchronous: the native side can continue reading from the PCM
        // pointer after request.Send() returns and encode it later on another queue. Because of
        // that, a single reusable NativeArray is unsafe here; the next AudioRead callback can
        // overwrite it while Opus/WebRTC is still consuming the previous frame.
        //
        // Keep one NativeArray per in-flight request and release it only after the matching
        // CaptureAudioFrame callback completes or is canceled.
        private readonly Dictionary<ulong, PendingAudioFrame> _pendingFrameData = new();
        private readonly object _pendingFrameDataLock = new object();

        private volatile bool _muted = false;
        public override bool Muted => _muted;

        private bool _started = false;
        private volatile bool _disposed = false;
        private int _audioReadCount = 0;

        // --- Temporary capture-rate diagnostics (Info level, emitted ~every 2s) ---
        // Measures the effective sample rate from wall-clock time vs the rate we declared to the
        // native source. A measured rate that differs from the declared rate means the format
        // label on the frames is wrong (audio would sound fast/slow/choppy on the receiver).
        private long _diagWindowStartTicks;     // 0 = not started
        private long _diagSamplesPerChannel;
        private int _diagAcceptedFrames;
        private int _diagDroppedFrames;

        // Device-capture sources (microphone, AudioSource taps) don't know their format ahead of
        // time — it is whatever Unity's audio graph delivers. They use this constructor, which
        // configures the native source from Unity's current output configuration.
        protected RtcAudioSource(RtcAudioSourceType audioSourceType)
            : this(audioSourceType, 0, 0) { }

        // Sources that generate a fixed, known format (e.g. test signal generators) declare it
        // directly. Passing 0 for either value falls back to the device configuration.
        protected RtcAudioSource(RtcAudioSourceType audioSourceType, uint sampleRate, uint channels)
        {
            _sourceType = audioSourceType;

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
        }

        // Reads Unity's actual output audio configuration. The capture path delivers buffers at the
        // DSP output rate/channel count (see AudioProbe), so this is the format the native source
        // must match. Falls back to the platform defaults when Unity cannot report a configuration
        // (e.g. batch mode without an audio device).
        private (uint sampleRate, uint channels) ResolveDeviceFormat()
        {
            uint sampleRate = _sourceType == RtcAudioSourceType.AudioSourceMicrophone
                ? DefaultMicrophoneSampleRate
                : DefaultSampleRate;
            uint channels = DefaultChannels;

            try
            {
                var config = UnityEngine.AudioSettings.GetConfiguration();
                if (config.sampleRate > 0)
                    sampleRate = (uint)config.sampleRate;
                var configuredChannels = SpeakerModeChannels(config.speakerMode);
                if (configuredChannels > 0)
                    channels = configuredChannels;
            }
            catch (Exception e)
            {
                Utils.Warning($"{DebugTag} could not read Unity audio configuration, using defaults: {e.Message}");
            }

            return (sampleRate, channels);
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
            _started = false;
            var pendingCount = PendingFrameCount();
            if (pendingCount > 0)
                Utils.Warning($"{DebugTag} stop requested with {pendingCount} pending capture callbacks");
            else
                Utils.Debug($"{DebugTag} stop");
        }

        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (_muted) return;
            if (_disposed) return;

            var frameIndex = Interlocked.Increment(ref _audioReadCount);
            if (channels <= 0)
            {
                Utils.Warning($"{DebugTag} dropping audio frame #{frameIndex} because channels={channels}");
                return;
            }

            if (data.Length == 0 || data.Length % channels != 0)
            {
                Utils.Warning($"{DebugTag} audio frame #{frameIndex} has invalid shape samples={data.Length} channels={channels}");
                return;
            }

            var willDrop = (uint)sampleRate != _expectedSampleRate || (uint)channels != _expectedChannels;
            RecordCaptureDiagnostics(data.Length / channels, channels, sampleRate, willDrop);

            // The native source rejects frames whose rate/channels differ from how it was
            // configured (it does not resample). This should not happen now that the source is
            // configured from the device, but if Unity reports an inconsistent format — or the
            // output configuration changes at runtime — we drop the frame instead of sending a
            // mismatch the native side would error on.
            if ((uint)sampleRate != _expectedSampleRate || (uint)channels != _expectedChannels)
            {
                if (frameIndex == 1 || frameIndex % 100 == 0)
                    Utils.Warning($"{DebugTag} dropping audio frame #{frameIndex}: format {sampleRate}/{channels} does not match source {_expectedSampleRate}/{_expectedChannels} (sourceType={_sourceType})");
                return;
            }

            var pendingBeforeSend = PendingFrameCount();
            if (frameIndex <= 3 || frameIndex % 100 == 0 || pendingBeforeSend >= 3)
            {
                Utils.Debug($"{DebugTag} capture frame #{frameIndex} samples={data.Length} channels={channels} sampleRate={sampleRate} pendingBeforeSend={pendingBeforeSend} thread={Thread.CurrentThread.ManagedThreadId}");
            }

            // Each captured frame gets its own backing buffer so the native encoder can safely
            // consume it asynchronously after request.Send() returns.
            var frameData = new NativeArray<short>(data.Length, Allocator.Persistent);

            // Copy from the audio read buffer into the frame buffer, converting
            // each sample to a 16-bit signed integer.
            static short FloatToS16(float v)
            {
                v *= 32768f;
                v = Math.Min(v, 32767f);
                v = Math.Max(v, -32768f);
                return (short)(v + Math.Sign(v) * 0.5f);
            }
            for (int i = 0; i < data.Length; i++)
                frameData[i] = FloatToS16(data[i]);

            // Capture the frame.
            using var request = FFIBridge.Instance.NewRequest<CaptureAudioFrameRequest>();
            using var audioFrameBufferInfo = request.TempResource<AudioFrameBufferInfo>();

            var pushFrame = request.request;
            pushFrame.SourceHandle = (ulong)Handle.DangerousGetHandle();
            pushFrame.Buffer = audioFrameBufferInfo;
            unsafe
            {
                 pushFrame.Buffer.DataPtr = (ulong)NativeArrayUnsafeUtility
                    .GetUnsafePtr(frameData);
            }
            pushFrame.Buffer.NumChannels = (uint)channels;
            pushFrame.Buffer.SampleRate = (uint)sampleRate;
            pushFrame.Buffer.SamplesPerChannel = (uint)data.Length / (uint)channels;

            // Wait for async callback, log an error if the capture fails. The callback's AsyncId
            // echoes the RequestAsyncId that Unity wrote onto the request.
            var requestAsyncId = request.RequestAsyncId;
            var pendingFrame = new PendingAudioFrame
            {
                FrameData = frameData,
                FrameIndex = frameIndex,
                SampleRate = sampleRate,
                Channels = channels,
                SampleCount = data.Length,
                StartedTimestamp = Stopwatch.GetTimestamp(),
            };
            lock (_pendingFrameDataLock)
            {
                _pendingFrameData[requestAsyncId] = pendingFrame;
            }

            void Callback(CaptureAudioFrameCallback callback)
            {
                if (callback.AsyncId != requestAsyncId) return;
                var completedFrame = ReleasePendingFrameData(requestAsyncId);
                if (completedFrame != null)
                {
                    var elapsedMs = ElapsedMilliseconds(completedFrame.StartedTimestamp);
                    if (callback.HasError)
                    {
                        Utils.Error($"{DebugTag} capture callback failed asyncId={requestAsyncId} frame={completedFrame.FrameIndex} elapsedMs={elapsedMs:F1} pendingAfter={PendingFrameCount()} error={callback.Error}");
                    }
                    else if (completedFrame.FrameIndex <= 3 || completedFrame.FrameIndex % 100 == 0 || elapsedMs > 100)
                    {
                        Utils.Debug($"{DebugTag} capture callback asyncId={requestAsyncId} frame={completedFrame.FrameIndex} elapsedMs={elapsedMs:F1} pendingAfter={PendingFrameCount()}");
                    }
                }
                if (callback.HasError)
                    Utils.Error($"{DebugTag} audio capture failed: {callback.Error}");
            }
            void OnCanceled()
            {
                var canceledFrame = ReleasePendingFrameData(requestAsyncId);
                if (canceledFrame != null)
                {
                    var elapsedMs = ElapsedMilliseconds(canceledFrame.StartedTimestamp);
                    Utils.Warning($"{DebugTag} capture callback canceled asyncId={requestAsyncId} frame={canceledFrame.FrameIndex} elapsedMs={elapsedMs:F1} pendingAfter={PendingFrameCount()}");
                }
            }

            FfiClient.Instance.RegisterPendingCallback(requestAsyncId, static e => e.CaptureAudioFrame, Callback, OnCanceled);
            try
            {
                using var response = request.Send();
            }
            catch
            {
                var failedFrame = ReleasePendingFrameData(requestAsyncId);
                if (failedFrame != null)
                {
                    Utils.Error($"{DebugTag} request send failed asyncId={requestAsyncId} frame={failedFrame.FrameIndex} pendingAfter={PendingFrameCount()}");
                }
                throw;
            }
        }

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

            var pendingCount = PendingFrameCount();
            if (pendingCount > 0)
                Utils.Warning($"{DebugTag} dispose(disposing={disposing}) with {pendingCount} pending capture callbacks");

            lock (_pendingFrameDataLock)
            {
                foreach (var pendingFrame in _pendingFrameData.Values)
                {
                    if (pendingFrame.FrameData.IsCreated)
                        pendingFrame.FrameData.Dispose();
                }
                _pendingFrameData.Clear();
            }
            Handle?.Dispose();
            _disposed = true;
            Utils.Debug($"{DebugTag} disposed");
        }

        private PendingAudioFrame ReleasePendingFrameData(ulong requestAsyncId)
        {
            PendingAudioFrame pendingFrame = null;
            lock (_pendingFrameDataLock)
            {
                if (_pendingFrameData.TryGetValue(requestAsyncId, out pendingFrame))
                    _pendingFrameData.Remove(requestAsyncId);
            }

            if (pendingFrame != null && pendingFrame.FrameData.IsCreated)
                pendingFrame.FrameData.Dispose();

            return pendingFrame;
        }

        private int PendingFrameCount()
        {
            lock (_pendingFrameDataLock)
            {
                return _pendingFrameData.Count;
            }
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

        private static double ElapsedMilliseconds(long startedTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startedTimestamp) * 1000.0 / Stopwatch.Frequency;
        }

        // Temporary diagnostic: accumulates captured audio over wall-clock time and, ~every 2s,
        // logs the effective sample rate vs the rate declared to the native source. Runs on the
        // audio thread; the periodic Info log is cheap.
        private void RecordCaptureDiagnostics(int samplesPerChannel, int channels, int sampleRate, bool dropped)
        {
            var now = Stopwatch.GetTimestamp();
            if (_diagWindowStartTicks == 0) _diagWindowStartTicks = now;
            _diagSamplesPerChannel += samplesPerChannel;
            if (dropped) _diagDroppedFrames++; else _diagAcceptedFrames++;

            var elapsed = (now - _diagWindowStartTicks) / (double)Stopwatch.Frequency;
            if (elapsed < 2.0) return;

            var measuredRate = _diagSamplesPerChannel / elapsed;
            Utils.Info($"{DebugTag} capture diag: declared={_expectedSampleRate}Hz/{_expectedChannels}ch measuredRate={measuredRate:F0}Hz " +
                       $"lastFrame={samplesPerChannel}smp/{channels}ch/{sampleRate}Hz accepted={_diagAcceptedFrames} dropped={_diagDroppedFrames} over={elapsed:F1}s");
            _diagWindowStartTicks = now;
            _diagSamplesPerChannel = 0;
            _diagAcceptedFrames = 0;
            _diagDroppedFrames = 0;
        }

        private string DebugTag => $"RtcAudioSource#{_debugId}";
    }
}
