using System;
using UnityEngine;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    /// <summary>
    /// An audio stream from a remote participant, attached to an <see cref="AudioSource"/>
    /// in the scene.
    /// </summary>
    public sealed class AudioStream : IDisposable
    {
        internal readonly FfiHandle Handle;
        private readonly AudioSource _audioSource;
        private readonly AudioProbe _probe;
        private RingBuffer _buffer;
        private short[] _tempBuffer;
        private uint _numChannels;
        private uint _sampleRate;
        private AudioResampler _resampler = new AudioResampler();
        private readonly object _lock = new object();
        private bool _disposed = false;

        // Pre-buffering state to prevent audio underruns
        private bool _isPrimed = false;
        private const float BufferSizeSeconds = 0.2f;  // 200ms ring buffer for all platforms
        private const float PrimingThresholdSeconds = 0.03f;  // Wait for 30ms of data before playing

        // Drift correction: skip samples when buffer fills up due to clock drift
        private const float HighWaterMarkPercent = 0.50f;    // Target 50% fill level after correction
        private const float SkipPerCallbackPercent = 0.05f;  // Skip 5% of callback samples per call

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

            using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
            var newAudioStream = request.request;
            newAudioStream.TrackHandle = (ulong)(audioTrack as ITrack).TrackHandle.DangerousGetHandle();
            newAudioStream.Type = AudioStreamType.AudioStreamNative;

            using var response = request.Send();
            FfiResponse res = response;
            Handle = FfiHandle.FromOwnedHandle(res.NewAudioStream.Stream.Handle);
            FfiClient.Instance.AudioStreamEventReceived += OnAudioStreamEvent;

            _audioSource = source;
            _probe = _audioSource.gameObject.AddComponent<AudioProbe>();
            _probe.AudioRead += OnAudioRead;
            _audioSource.Play();

            // Subscribe to application pause events to handle background/foreground transitions
            MonoBehaviourContext.OnApplicationPauseEvent += OnApplicationPause;
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
                // Initialize or reinitialize buffer if audio format changed
                if (_buffer == null || channels != _numChannels || sampleRate != _sampleRate || data.Length != _tempBuffer.Length)
                {
                    // Always use 200ms ring buffer for all platforms
                    int bufferSize = (int)(channels * sampleRate * BufferSizeSeconds);
                    _buffer?.Dispose();
                    _buffer = new RingBuffer(bufferSize * sizeof(short));
                    _tempBuffer = new short[data.Length];
                    _numChannels = (uint)channels;
                    _sampleRate = (uint)sampleRate;

                    // Buffer was recreated, need to re-prime
                    _isPrimed = false;
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
                    Utils.Debug($"AudioStream underrun detected, re-priming (got {valuesAvailableToRead} samples)");

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

                // Drift correction: if buffer is filling up (producer faster than consumer),
                // skip a small number of samples to prevent overflow and keep latency bounded.
                int highWaterBytes = (int)(_buffer.Capacity * HighWaterMarkPercent);
                int remainingBytes = _buffer.AvailableRead();
                if (remainingBytes > highWaterBytes)
                {
                    int skipBytes = (int)(data.Length * sizeof(short) * SkipPerCallbackPercent);
                    int frameSize = channels * sizeof(short);
                    skipBytes -= skipBytes % frameSize; // align to frame boundary

                    if (skipBytes > 0)
                    {
                        _buffer.SkipRead(skipBytes);
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

        // Called when application goes to background or returns to foreground
        private void OnApplicationPause(bool pause)
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
                if (_numChannels == 0)
                    return;

                unsafe
                {
                    using var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);
                    if (uFrame != null)
                    {
                        var data = new Span<byte>(uFrame.Data.ToPointer(), uFrame.Length);
                        _buffer?.Write(data);
                    }

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
            // touching partially disposed state.
            FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;
            MonoBehaviourContext.OnApplicationPauseEvent -= OnApplicationPause;

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
                _resampler?.Dispose();
                _resampler = null;
                Handle.Dispose();
            }

            _disposed = true;
        }

        ~AudioStream()
        {
            Dispose(false);
        }
    }
}
