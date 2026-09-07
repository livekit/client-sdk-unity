using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Internal.FFI;
using LiveKit.Internal.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// The processing stage behind <see cref="AudioProcessingOptions"/> on an
    /// <see cref="RtcAudioSource"/>: owns one <see cref="AudioProcessingModule"/>, re-chunks the
    /// capture and the playout reference into 10 ms frames, and hands every processed capture
    /// chunk to the source for the FFI.
    /// </summary>
    /// <remarks>
    /// Threading. <see cref="TryProcessCapture"/> (from the source's audio callback) and
    /// <see cref="OnPlayoutAudio"/> (from <see cref="PlayoutReference"/>) both run on the Unity
    /// audio thread. Within one DSP tick the capture probes run before the listener tap, so the
    /// reference for tick N arrives after the capture of tick N; that is fine because the acoustic
    /// echo of tick N's playout only reaches the microphone several ticks later. <see cref="Start"/>,
    /// <see cref="Stop"/> and the maintenance coroutine run on the main thread and never touch the
    /// ring buffers; they raise flags the audio thread acts on. Nothing here logs from the audio
    /// thread — diagnostics are counters, read via <see cref="GetStats"/>.
    ///
    /// The Rust side asserts (and takes the process down) on a frame that is not a whole multiple
    /// of 10 ms, so the chunking here is not optional, and rates whose 10 ms chunk is not a whole
    /// number of samples are bypassed entirely.
    /// </remarks>
    internal sealed class AudioProcessor : IDisposable
    {
        /// <summary>Receives one processed 10 ms chunk and takes ownership of the array.</summary>
        internal delegate void ProcessedFrameSink(NativeArray<short> frame, int channels, int sampleRate);

        // Ring capacity in chunks. Bounds the latency added when a DSP block is not a multiple of 10 ms.
        private const int BufferedChunks = 8;
        private const float MaintenanceIntervalSeconds = 2f;

        private readonly AudioProcessingModule _apm;
        private readonly ProcessedFrameSink _sink;
        private readonly bool _echoCancellation;

        // Guards the pinned reference chunk against Dispose racing an in-flight callback. The
        // capture side needs no lock: each chunk lives in its own NativeArray owned by the sink.
        private readonly object _referenceLock = new object();

        // Capture side. Audio thread only.
        private PcmRingBuffer _captureRing;
        private short[] _captureStaging;
        private short[] _captureChunk;
        private int _captureRate;
        private int _captureChannels;
        private int _captureChunkSamples;

        // Reference side. Audio thread only, under _referenceLock. The chunk stays pinned so the
        // module has a stable address to process in place.
        private PcmRingBuffer _referenceRing;
        private short[] _referenceStaging;
        private short[] _referenceChunk;
        private GCHandle _referenceChunkPin;
        private int _referenceRate;
        private int _referenceChannels;
        private int _referenceChunkSamples;

        // Cross-thread flags.
        private volatile bool _running;
        private volatile bool _bypass;
        private volatile bool _disposed;
        private int _captureResetRequested;
        private int _referenceResetRequested;
        private int _unsupportedRateWarned;
        private int _maintenanceGeneration;

        // Counters.
        private long _captureChunks;
        private long _referenceChunks;
        private long _failedChunks;
        private volatile string _lastError;
        private volatile int _delayHintMs = -1;

        /// <exception cref="InvalidOperationException">The FFI could not create the module.</exception>
        public AudioProcessor(AudioProcessingOptions options, ProcessedFrameSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _echoCancellation = options.EchoCancellation;
            _apm = new AudioProcessingModule(
                echoCancellerEnabled: options.EchoCancellation,
                gainControllerEnabled: options.AutoGainControl,
                highPassFilterEnabled: options.HighPassFilter,
                noiseSuppressionEnabled: options.NoiseSuppression);
        }

        /// <summary>Main thread.</summary>
        public void Start()
        {
            if (_disposed || _running) return;
            _running = true;
            RequestReset();

            if (_echoCancellation)
            {
                PlayoutReference.AudioRead += OnPlayoutAudio;
                PlayoutReference.Acquire();
            }

            SeedDelayHint();
            MonoBehaviourContext.RunCoroutine(Maintenance(++_maintenanceGeneration));
        }

        /// <summary>Main thread.</summary>
        public void Stop()
        {
            if (!_running) return;
            _running = false;

            if (_echoCancellation)
            {
                PlayoutReference.AudioRead -= OnPlayoutAudio;
                PlayoutReference.Release();
            }
        }

        /// <summary>
        /// Any thread. Clears both feeds before their next audio callback. Call when the capture
        /// path restarts (e.g. a microphone resume) so stale samples do not misalign the canceller.
        /// </summary>
        public void RequestReset()
        {
            Interlocked.Exchange(ref _captureResetRequested, 1);
            Interlocked.Exchange(ref _referenceResetRequested, 1);
        }

        // Periodic main-thread upkeep: re-attach the reference after scene or device changes and
        // refresh the delay hint (iOS reports zero session latency until the session is active).
        private IEnumerator Maintenance(int generation)
        {
            while (_running && !_disposed && generation == _maintenanceGeneration)
            {
                if (_echoCancellation) PlayoutReference.EnsureAttached();
                SeedDelayHint();
                yield return new WaitForSeconds(MaintenanceIntervalSeconds);
            }
        }

        private void SeedDelayHint()
        {
            if (!_echoCancellation || _disposed) return;

            var hint = AudioProcessingDelayHint.EstimateMs();
            if (hint == _delayHintMs) return;

            string error;
            try
            {
                error = _apm.SetStreamDelayMs(hint);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (error != null)
            {
                Utils.Warning($"AudioProcessor: set_stream_delay_ms({hint}) failed: {error}");
                return;
            }

            _delayHintMs = hint;
        }

        /// <summary>
        /// Unity audio thread. Runs the block through the module in 10 ms chunks and forwards each
        /// chunk to the sink. Returns false when the block must go out unprocessed instead:
        /// processing is stopped, or bypassed for this sample rate.
        /// </summary>
        public bool TryProcessCapture(float[] data, int channels, int sampleRate)
        {
            if (_disposed || !_running || _bypass) return false;
            if (data == null || data.Length == 0 || channels <= 0 || sampleRate <= 0) return false;

            if (!AudioProcessingModule.IsSupportedApiRate(sampleRate))
            {
                _bypass = true;
                WarnUnsupportedRate(sampleRate);
                return false;
            }

            if (Interlocked.Exchange(ref _captureResetRequested, 0) == 1)
                _captureRing?.Clear();

            if (_captureStaging == null || sampleRate != _captureRate || channels != _captureChannels || _captureStaging.Length < data.Length)
                ConfigureCapture(sampleRate, channels, data.Length);

            for (var i = 0; i < data.Length; i++)
                _captureStaging[i] = PcmConvert.FloatToS16(data[i]);
            _captureRing.Write(_captureStaging, 0, data.Length);

            while (_captureRing.TryDrain(_captureChunk, _captureChunkSamples))
            {
                var frame = new NativeArray<short>(_captureChunkSamples, Allocator.Persistent);
                frame.CopyFrom(_captureChunk);
                ProcessCaptureChunk(frame, sampleRate, channels);
                Interlocked.Increment(ref _captureChunks);
                _sink(frame, channels, sampleRate);
            }

            return true;
        }

        private void ProcessCaptureChunk(NativeArray<short> frame, int sampleRate, int channels)
        {
            try
            {
                IntPtr ptr;
                unsafe
                {
                    ptr = (IntPtr)NativeArrayUnsafeUtility.GetUnsafePtr(frame);
                }
                var error = _apm.ProcessStream(ptr, frame.Length * sizeof(short), sampleRate, channels);
                if (error != null) RecordFailure(error);
            }
            catch (Exception e)
            {
                // The chunk goes out unprocessed rather than not at all.
                RecordFailure(e.Message);
            }
        }

        // Unity audio thread, from PlayoutReference. Must not modify data.
        private void OnPlayoutAudio(float[] data, int channels, int sampleRate)
        {
            if (_disposed || !_running || _bypass) return;
            if (data == null || data.Length == 0 || channels <= 0) return;
            if (!AudioProcessingModule.IsSupportedApiRate(sampleRate)) return;

            lock (_referenceLock)
            {
                if (_disposed) return;

                if (Interlocked.Exchange(ref _referenceResetRequested, 0) == 1)
                    _referenceRing?.Clear();

                if (_referenceStaging == null || sampleRate != _referenceRate || channels != _referenceChannels || _referenceStaging.Length < data.Length)
                    ConfigureReference(sampleRate, channels, data.Length);

                for (var i = 0; i < data.Length; i++)
                    _referenceStaging[i] = PcmConvert.FloatToS16(data[i]);
                _referenceRing.Write(_referenceStaging, 0, data.Length);

                var byteCount = _referenceChunkSamples * sizeof(short);
                while (_referenceRing.TryDrain(_referenceChunk, _referenceChunkSamples))
                {
                    try
                    {
                        var error = _apm.ProcessReverseStream(_referenceChunkPin.AddrOfPinnedObject(), byteCount, sampleRate, channels);
                        if (error != null) RecordFailure(error);
                    }
                    catch (Exception e)
                    {
                        RecordFailure(e.Message);
                    }
                    Interlocked.Increment(ref _referenceChunks);
                }
            }
        }

        // Format changes are rare (first block, device switch); the allocations here are accepted
        // on the audio thread for the same reason AudioStream sizes its buffers lazily.
        private void ConfigureCapture(int sampleRate, int channels, int incomingSamples)
        {
            var chunkSamples = AudioProcessingModule.FrameSizeFor(sampleRate) * channels;
            _captureRate = sampleRate;
            _captureChannels = channels;
            _captureChunkSamples = chunkSamples;
            _captureChunk = new short[chunkSamples];
            _captureStaging = new short[incomingSamples];
            // Never smaller than one input block, or a large block would overflow immediately.
            _captureRing = new PcmRingBuffer(Math.Max(chunkSamples * BufferedChunks, incomingSamples + chunkSamples));
        }

        private void ConfigureReference(int sampleRate, int channels, int incomingSamples)
        {
            var chunkSamples = AudioProcessingModule.FrameSizeFor(sampleRate) * channels;
            _referenceRate = sampleRate;
            _referenceChannels = channels;
            _referenceChunkSamples = chunkSamples;

            if (_referenceChunkPin.IsAllocated) _referenceChunkPin.Free();
            _referenceChunk = new short[chunkSamples];
            _referenceChunkPin = GCHandle.Alloc(_referenceChunk, GCHandleType.Pinned);

            _referenceStaging = new short[incomingSamples];
            _referenceRing = new PcmRingBuffer(Math.Max(chunkSamples * BufferedChunks, incomingSamples + chunkSamples));
        }

        private void RecordFailure(string error)
        {
            _lastError = error;
            Interlocked.Increment(ref _failedChunks);
        }

        // Logging is not allowed on the audio thread; hand the message to the main thread.
        private void WarnUnsupportedRate(int sampleRate)
        {
            if (Interlocked.Exchange(ref _unsupportedRateWarned, 1) == 1) return;

            var message = $"AudioProcessor: Unity's output sample rate {sampleRate} Hz has no whole-sample 10 ms chunk; " +
                          "audio processing is bypassed and the capture is published unprocessed.";
            var context = FfiClient.Instance._context;
            if (context != null)
                context.Post(static m => Utils.Warning(m), message);
            else
                Utils.Warning(message);
        }

        public AudioProcessingStats GetStats() => new AudioProcessingStats(
            active: _running && !_bypass && !_disposed,
            referenceAttached: _echoCancellation && PlayoutReference.IsAttached,
            captureSampleRate: _captureRate,
            captureChannels: _captureChannels,
            referenceSampleRate: _referenceRate,
            referenceChannels: _referenceChannels,
            captureChunks: Interlocked.Read(ref _captureChunks),
            referenceChunks: Interlocked.Read(ref _referenceChunks),
            droppedCaptureSamples: _captureRing?.OverflowSamples ?? 0,
            droppedReferenceSamples: _referenceRing?.OverflowSamples ?? 0,
            failedChunks: Interlocked.Read(ref _failedChunks),
            lastError: _lastError,
            streamDelayHintMs: _delayHintMs);

        public void Dispose()
        {
            if (_disposed) return;

            Stop();
            lock (_referenceLock)
            {
                _disposed = true;
                if (_referenceChunkPin.IsAllocated) _referenceChunkPin.Free();
                _referenceChunk = null;
                _referenceRing = null;
            }
            _apm.Dispose();
        }
    }
}
