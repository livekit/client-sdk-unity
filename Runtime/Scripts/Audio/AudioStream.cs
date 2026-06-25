using System;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using LiveKit.Internal.FFI.Requests;

using LiveKit.Internal.Threading;
namespace LiveKit
{
    /// <summary>
    /// An audio stream from a remote participant, attached to an <see cref="AudioSource"/>
    /// in the scene.
    /// </summary>
    public sealed class AudioStream : IDisposable
    {
        // FFI native stream is created lazily on the first OnAudioRead so we can pass
        // Unity's actual delivered (channels, sampleRate) — not a system-speaker-mode
        // guess. _handle is null until CreateOrRecreateFfiStream completes on the main
        // thread. The same path runs again whenever Unity's delivered format changes
        // mid-stream (e.g. after a system audio device switch).
        private FfiHandle _handle;
        internal FfiHandle Handle => _handle;
        private readonly ulong _trackHandleId;
        private uint _ffiNumChannels;
        private uint _ffiSampleRate;
        private bool _pendingFfiRequest;

        private readonly AudioSource _audioSource;
        private AudioProbe _probe;
        private RingBuffer _buffer;
        private short[] _tempBuffer;
        private short[] _crossfadeScratch;
        private uint _numChannels;
        private uint _sampleRate;
        private readonly object _lock = new object();
        private bool _disposed = false;

        // Pre-buffering state to prevent audio underruns
        private bool _isPrimed = false;
        private const float BufferSizeSeconds = 0.2f;  // 200ms ring buffer for all platforms
        private const float PrimingThresholdSeconds = 0.03f;  // Wait for 30ms of data before playing

        // Drift correction: skip samples when the buffer fills up due to clock drift.
        // HWM at 50% (100ms of 200ms) so normal network jitter does not trip catch-up.
        // Cooldown prevents back-to-back skips, which sound like a gravelly click train;
        // one occasional skip is inaudible thanks to the crossfade in OnAudioRead.
        private const float HighWaterMarkPercent = 0.50f;
        private const float SkipPerCallbackPercent = 0.05f;
        private const int SkipCooldownCallbacks = 10;
        private const int CrossfadeFrames = 128;  // ~2.7ms @ 48kHz
        private int _skipCooldown = 0;

        /// <summary>
        /// Creates a new audio stream from a remote audio track, attaching it to the
        /// given <see cref="AudioSource"/> in the scene.
        /// </summary>
        /// <param name="audioTrack">The remote audio track to stream.</param>
        /// <param name="source">The audio source to play the stream on.</param>
        /// <exception cref="InvalidOperationException">Thrown if the audio track's room or
        /// participant is invalid.</exception>
        public AudioStream(RemoteAudioTrack audioTrack, AudioSource source)
        {
            if (!audioTrack.Room.TryGetTarget(out var room))
                throw new InvalidOperationException("audiotrack's room is invalid");

            if (!audioTrack.Participant.TryGetTarget(out var participant))
                throw new InvalidOperationException("audiotrack's participant is invalid");

            _trackHandleId = (ulong)(audioTrack as ITrack).TrackHandle.DangerousGetHandle();

            _audioSource = source;
            _probe = _audioSource.gameObject.AddComponent<AudioProbe>();
            _probe.AudioRead += OnAudioRead;
            _audioSource.Play();

            // Subscribe to application pause events to handle background/foreground transitions
            MonoBehaviourContext.OnApplicationPauseEvent += OnApplicationPause;

            // Unity stops every AudioSource when the system audio output device changes
            // (e.g. headphones unplugged). Without re-playing the source, OnAudioFilterRead
            // stops firing and the stream goes silent until the AudioStream is recreated.
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;

            // FFI stream creation is deferred to the first OnAudioRead. We subscribe to
            // AudioStreamEventReceived only after the stream exists (CreateFfiStream).
        }

        // Called on the main thread (posted from OnAudioRead via FfiClient._context) when
        // either there is no FFI stream yet or Unity's delivered (channels, sampleRate) no
        // longer matches what we asked Rust for. Builds a fresh native stream and swaps it
        // in atomically. The old handle is disposed AFTER the swap so any in-flight frames
        // from the old stream fail the handle-id filter in OnAudioStreamEvent.
        private void CreateOrRecreateFfiStream(uint observedChannels, uint observedSampleRate)
        {
            lock (_lock) { if (_disposed) return; }

            FfiHandle newHandle;
            try
            {
                using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
                var req = request.request;
                req.TrackHandle = _trackHandleId;
                req.Type = AudioStreamType.AudioStreamNative;
                req.SampleRate = observedSampleRate;
                req.NumChannels = observedChannels;

                using var response = request.Send();
                FfiResponse res = response;
                newHandle = FfiHandle.FromOwnedHandle(res.NewAudioStream.Stream.Handle);
            }
            catch (Exception ex)
            {
                Utils.Error($"AudioStream FFI (re)create failed: {ex}");
                lock (_lock) { _pendingFfiRequest = false; }
                return;
            }

            FfiHandle oldHandle;
            bool firstCreate;
            lock (_lock)
            {
                if (_disposed) { newHandle.Dispose(); return; }
                oldHandle = _handle;
                firstCreate = oldHandle == null;
                _handle = newHandle;
                _ffiNumChannels = observedChannels;
                _ffiSampleRate = observedSampleRate;
                _buffer?.Clear();
                _isPrimed = false;
                _skipCooldown = 0;
                _pendingFfiRequest = false;

                if (firstCreate)
                    FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;
            }
            oldHandle?.Dispose();
        }

        // Called on Unity audio thread
        private void OnAudioRead(float[] data, int channels, int sampleRate)
        {
            if (_disposed)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            lock (_lock)
            {
                // Single gate covering first-create and runtime format changes (e.g. after a
                // system audio device switch). When the FFI stream is missing or what we asked
                // Rust for no longer matches what Unity is delivering, post a (re)create to the
                // main thread and output silence until it lands. The priming window absorbs this.
                if (_handle == null || channels != _ffiNumChannels || sampleRate != _ffiSampleRate)
                {
                    if (!_pendingFfiRequest)
                    {
                        _pendingFfiRequest = true;
                        uint observedCh = (uint)channels;
                        uint observedSr = (uint)sampleRate;
                        FfiClient.Instance._context?.Post(_ => CreateOrRecreateFfiStream(observedCh, observedSr), null);
                    }
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                // Initialize or reinitialize buffer if audio format changed
                if (_buffer == null || channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    // Always use 200ms ring buffer for all platforms
                    int bufferSize = (int)(channels * sampleRate * BufferSizeSeconds);
                    _buffer?.Dispose();
                    _buffer = new RingBuffer(bufferSize * sizeof(short));
                    _tempBuffer = new short[data.Length];
                    _crossfadeScratch = new short[CrossfadeFrames * channels];
                    _numChannels = (uint)channels;
                    _sampleRate = (uint)sampleRate;

                    // Buffer was recreated, need to re-prime
                    _isPrimed = false;
                    _skipCooldown = 0;
                }

                static float S16ToFloat(short v)
                {
                    return v / 32768f;
                }

                // Check if we have enough data in the buffer to start/continue playback.
                // Pre-buffering strategy: wait for 30ms of data before playing to avoid underruns.
                int primingThresholdBytes = (int)(channels * sampleRate * PrimingThresholdSeconds * sizeof(short));
                int availableBytes = _buffer.AvailableRead();

                if (!_isPrimed)
                {
                    // Not yet primed - check if we have enough data to start playing
                    if (availableBytes >= primingThresholdBytes)
                    {
                        _isPrimed = true;
                        Utils.Debug($"AudioStream primed with {availableBytes} bytes ({availableBytes / (channels * sampleRate * sizeof(short)) * 1000f:F1}ms)");
                    }
                    else
                    {
                        // Not enough data yet, output silence and wait
                        Array.Clear(data, 0, data.Length);
                        return;
                    }
                }

                int valuesAvailableToRead = _buffer.AvailableRead() / sizeof(short);
                // Underrun detection: If we couldn't read enough samples, immediately output silence
                // and wait for the buffer to refill enough to offer Unity a full sample.
                // This prevents choppy audio from playing partial samples during underrun.
                if (valuesAvailableToRead < data.Length)
                {
                    _isPrimed = false;
                    Utils.Debug($"AudioStream underrun detected, re-priming (got {valuesAvailableToRead} samples but want to read {data.Length})");

                    // Output silence immediately instead of playing partial/choppy samples.
                    // On next frames, the !_isPrimed check above will ensure we wait for 30ms
                    // of data before resuming playback smoothly.
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                // Try to read audio samples from the ring buffer into our temp buffer.
                // The ring buffer acts as a jitter buffer between:
                // - Rust FFI pushing frames (on main thread, with network timing)
                // - Unity audio thread pulling samples (real-time, consistent timing)
                var temp = MemoryMarshal.Cast<short, byte>(_tempBuffer.AsSpan().Slice(0, data.Length));
                int bytesRead = _buffer.Read(temp);

                // Calculate how many samples (shorts) were actually read from the ring buffer.
                // If the buffer is empty or doesn't have enough data, bytesRead will be less than requested.
                int samplesRead = bytesRead / sizeof(short);

                // Drift correction: if the buffer is filling up (producer faster than
                // consumer), discard a small run of samples and crossfade the output tail
                // into the post-skip window so the seam is inaudible. Cooldown spaces
                // skips out so we never produce back-to-back artifacts.
                if (_skipCooldown > 0)
                    _skipCooldown--;

                int highWaterBytes = (int)(_buffer.Capacity * HighWaterMarkPercent);
                if (_skipCooldown == 0 && _buffer.AvailableRead() > highWaterBytes)
                {
                    int frameSize = channels * sizeof(short);
                    int skipBytes = (int)(data.Length * sizeof(short) * SkipPerCallbackPercent);
                    skipBytes -= skipBytes % frameSize; // align to frame boundary

                    int crossfadeShorts = CrossfadeFrames * channels;
                    int crossfadeBytes = crossfadeShorts * sizeof(short);

                    // Only skip if we still have enough data left to fill the crossfade
                    // window; never trade a catch-up artifact for an underrun artifact.
                    if (skipBytes > 0 && _buffer.AvailableRead() >= skipBytes + crossfadeBytes)
                    {
                        _buffer.SkipRead(skipBytes);

                        var postBytes = MemoryMarshal.Cast<short, byte>(_crossfadeScratch.AsSpan().Slice(0, crossfadeShorts));
                        _buffer.Read(postBytes);

                        // Linearly crossfade the last crossfadeFrames frames of _tempBuffer
                        // with the post-skip samples: the step discontinuity becomes a
                        // short linear ramp that is continuous with the next callback.
                        int tailStart = samplesRead - crossfadeShorts;
                        if (tailStart >= 0)
                        {
                            for (int i = 0; i < crossfadeShorts; i++)
                            {
                                int frameIdx = i / channels;
                                float t = (frameIdx + 1) / (float)CrossfadeFrames;
                                short pre = _tempBuffer[tailStart + i];
                                short post = _crossfadeScratch[i];
                                _tempBuffer[tailStart + i] = (short)(pre * (1f - t) + post * t);
                            }
                            _skipCooldown = SkipCooldownCallbacks;
                        }
                    }
                }

                // Clear the entire output buffer to silence, then fill with the samples
                // we successfully read from the ring buffer.
                Array.Clear(data, 0, data.Length);
                for (int i = 0; i < samplesRead; i++)
                {
                    data[i] = S16ToFloat(_tempBuffer[i]);
                }
            }
        }

        // Called when the system audio output device changes (e.g. plug/unplug headphones)
        // or AudioSettings.Reset is invoked. Unity tears down its audio engine in both cases,
        // which stops every AudioSource and detaches the AudioProbe filter from the rebuilt
        // audio graph. Just calling Play() on the existing source isn't always enough; we
        // additionally recreate the AudioProbe so Unity re-registers the OnAudioFilterRead
        // node on the new graph.
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (_disposed) return;

            lock (_lock)
            {
                _buffer?.Clear();
                _isPrimed = false;
            }

            if (_probe != null)
            {
                _probe.AudioRead -= OnAudioRead;
                UnityEngine.Object.Destroy(_probe);
            }
            _probe = _audioSource.gameObject.AddComponent<AudioProbe>();
            _probe.AudioRead += OnAudioRead;

            _audioSource.Stop();
            _audioSource.Play();
        }

        // Called when application goes to background or returns to foreground
        internal void OnApplicationPause(bool pause)
        {
            if (_disposed)
                return;

            // When returning from background, clear the ring buffer and reset priming state.
            // This ensures we don't play stale audio data and forces the stream to wait
            // for fresh data (30ms) before resuming playback, preventing audio glitches.
            // Also set a timestamp to discard any stale FFI callbacks that arrive shortly after.
            if (!pause)  // Returning to foreground
            {
                lock (_lock)
                {
                    if (_buffer != null)
                    {
                        _buffer.Clear();
                        _isPrimed = false;
                        Utils.Debug("AudioStream cleared buffer on app resume, waiting to re-prime");
                    }
                }
            }
        }

        // Called on FFI callback thread       
        private void OnAudioStreamEvent(AudioStreamEvent e)
        {
            if (_disposed)
                return;

            if ((ulong)Handle.DangerousGetHandle() != e.StreamHandle)
                return;

            if (e.MessageCase != AudioStreamEvent.MessageOneofCase.FrameReceived)
                return;

            using var frame = new AudioFrame(e.FrameReceived.Frame);

            lock (_lock)
            {
                // _pendingFfiRequest gates writes during a (re)create: between the moment
                // OnAudioRead detects a format mismatch and the swap landing, Rust is still
                // emitting frames at the OLD format. Drop them to avoid corrupting the buffer.
                // The handle-id filter above is the second line of defense for stragglers
                // arriving from the old stream after the swap.
                if (_buffer == null || _pendingFfiRequest)
                    return;

                unsafe
                {
                    var data = new ReadOnlySpan<byte>(frame.Data.ToPointer(), frame.Length);
                    _buffer.Write(data);
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            // Remove long-lived delegate references first so this instance can become collectible
            // as soon as user code drops it. This also prevents late native callbacks from
            // touching partially disposed state. AudioStreamEventReceived was only subscribed
            // after CreateFfiStream succeeded; -= against an unsubscribed handler is a no-op,
            // but the explicit guard documents the lifecycle.
            if (_handle != null)
                FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
            MonoBehaviourContext.OnApplicationPauseEvent -= OnApplicationPause;
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;

            lock (_lock)
            {
                // Native resources can be released on both the explicit-dispose and finalizer
                // paths. Unity objects are only touched when Dispose() is called explicitly on the
                // main thread.
                if (disposing)
                {
                    _audioSource.Stop();
                    if (_probe != null)
                    {
                        _probe.AudioRead -= OnAudioRead;
                        UnityEngine.Object.Destroy(_probe);
                    }
                }

                _buffer?.Dispose();
                _buffer = null;
                _tempBuffer = null;
                _crossfadeScratch = null;
                _handle?.Dispose();
                _handle = null;
            }

            _disposed = true;
        }

        ~AudioStream()
        {
            Dispose(false);
        }

        // For testing and debugging
        internal float GetBufferFill()
        {
            lock(_lock)
            {
                if (_buffer == null)
                    return 0;
                return _buffer.AvailableReadInPercent();
            }
                
        }
    }
}
