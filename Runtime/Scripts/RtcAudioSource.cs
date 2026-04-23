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

#if (UNITY_IOS || (UNITY_ANDROID && UNITY_6000_0_OR_NEWER)) && !UNITY_EDITOR
        // iOS/Android(Unity 6000+) microphone sample rate is 24k
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

        protected RtcAudioSource(int channels = 2, RtcAudioSourceType audioSourceType = RtcAudioSourceType.AudioSourceCustom)
        {
            _sourceType = audioSourceType;
            _expectedChannels = (uint)channels;

            using var request = FFIBridge.Instance.NewRequest<NewAudioSourceRequest>();
            var newAudioSource = request.request;
            newAudioSource.Type = AudioSourceType.AudioSourceNative;
            newAudioSource.NumChannels = (uint)channels;
            newAudioSource.SampleRate = _sourceType == RtcAudioSourceType.AudioSourceMicrophone ?
                DefaultMicrophoneSampleRate : DefaultSampleRate;
            _expectedSampleRate = newAudioSource.SampleRate;

            UnityEngine.Debug.Log($"NewAudioSource: {newAudioSource.NumChannels} {newAudioSource.SampleRate}");

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

            if ((uint)sampleRate != _expectedSampleRate || (uint)channels != _expectedChannels)
            {
                Utils.Warning($"{DebugTag} audio frame #{frameIndex} metadata mismatch actualRate={sampleRate} actualChannels={channels} expectedRate={_expectedSampleRate} expectedChannels={_expectedChannels} sourceType={_sourceType}");
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

        private string DebugTag => $"RtcAudioSource#{_debugId}";
    }
}
