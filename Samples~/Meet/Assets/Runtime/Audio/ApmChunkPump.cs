using System;
using System.Runtime.InteropServices;
using LiveKit;

/// <summary>Processes one 10 ms interleaved int16 chunk in place at <paramref name="dataPtr"/>.</summary>
internal delegate string ApmChunkHandler(IntPtr dataPtr, int byteCount, int sampleRate, int channels);

/// <summary>Receives one processed 10 ms chunk as interleaved floats.</summary>
internal delegate void ProcessedChunkHandler(float[] data, int channels, int sampleRate);

/// <summary>
/// Re-chunks a variable-size PCM feed into the fixed 10 ms frames the APM requires, processes
/// each one in place, and optionally hands the result on.
///
/// Neither feed is 10 ms natively: capture arrives in DSP-buffer-sized blocks (1024 frames
/// ≈ 21.3 ms at 48 kHz) and render frames arrive at whatever the decoder emits. Geometry is
/// taken from the incoming buffers, never from declared constants.
///
/// Allocation-free once the format has settled. The chunk buffer stays pinned for the pump's
/// lifetime so the APM has a stable address to process in place.
/// </summary>
internal sealed class ApmChunkPump : IDisposable
{
    private readonly ApmChunkHandler _process;
    private readonly ProcessedChunkHandler _onProcessed;
    private readonly int _bufferedChunks;

    private PcmRingBuffer _ring;
    private short[] _chunk;
    private GCHandle _chunkPin;
    private IntPtr _chunkPtr;
    private int _chunkSamples;
    private int _chunkBytes;

    private short[] _staging;
    private float[] _processed;

    private int _sampleRate;
    private int _channels;
    private bool _disposed;

    public int SampleRate => _sampleRate;
    public int Channels => _channels;
    public int ChunkSamples => _chunkSamples;
    public int ProcessedChunkCount { get; private set; }
    public int FailedChunkCount { get; private set; }
    public int DroppedSamples => _ring?.OverflowSamples ?? 0;
    public string LastError { get; private set; }

    /// <param name="process">Processes one chunk in place; returns an error string or null.</param>
    /// <param name="onProcessed">Optional consumer of the processed chunk (capture side only).</param>
    /// <param name="bufferedChunks">
    /// Ring capacity in 10 ms chunks. Sets the worst-case added latency, so keep it just large
    /// enough to absorb one input block plus jitter.
    /// </param>
    public ApmChunkPump(ApmChunkHandler process, ProcessedChunkHandler onProcessed = null, int bufferedChunks = 8)
    {
        if (bufferedChunks <= 0) throw new ArgumentOutOfRangeException(nameof(bufferedChunks));
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _onProcessed = onProcessed;
        _bufferedChunks = bufferedChunks;
    }

    /// <summary>Feeds interleaved floats (Unity capture path).</summary>
    public void Push(float[] data, int channels, int sampleRate)
    {
        if (_disposed || data == null || data.Length == 0) return;
        if (!EnsureFormat(sampleRate, channels, data.Length)) return;

        if (_staging == null || _staging.Length < data.Length) _staging = new short[data.Length];
        for (var i = 0; i < data.Length; i++) _staging[i] = FloatToS16(data[i]);

        _ring.Write(_staging, 0, data.Length);
        Drain();
    }

    /// <summary>Feeds interleaved int16 straight from a native buffer (FFI render path).</summary>
    public void Push(IntPtr dataPtr, int samplesPerChannel, int channels, int sampleRate)
    {
        if (_disposed || dataPtr == IntPtr.Zero || samplesPerChannel <= 0 || channels <= 0) return;

        var total = samplesPerChannel * channels;
        if (!EnsureFormat(sampleRate, channels, total)) return;

        if (_staging == null || _staging.Length < total) _staging = new short[total];
        Marshal.Copy(dataPtr, _staging, 0, total);

        _ring.Write(_staging, 0, total);
        Drain();
    }

    public void Reset() => _ring?.Clear();

    // An FFI failure must not escape: on the capture side this runs inside OnAudioFilterRead, and
    // an exception there takes out Unity's audio callback. The unprocessed chunk is forwarded
    // instead, so a broken APM degrades to no cancellation rather than to no audio.
    private void Drain()
    {
        while (_ring.TryDrain(_chunk, _chunkSamples))
        {
            try
            {
                var error = _process(_chunkPtr, _chunkBytes, _sampleRate, _channels);
                if (error != null) LastError = error;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                FailedChunkCount++;
            }

            ProcessedChunkCount++;

            if (_onProcessed == null) continue;

            for (var i = 0; i < _chunkSamples; i++) _processed[i] = _chunk[i] / 32768f;
            _onProcessed(_processed, _channels, _sampleRate);
        }
    }

    private bool EnsureFormat(int sampleRate, int channels, int incomingSamples)
    {
        if (sampleRate <= 0 || channels <= 0) return false;
        if (sampleRate == _sampleRate && channels == _channels) return true;

        var frameSize = AudioProcessingModule.FrameSizeFor(sampleRate);
        if (frameSize <= 0) return false;

        ReleaseChunk();

        _sampleRate = sampleRate;
        _channels = channels;
        _chunkSamples = frameSize * channels;
        _chunkBytes = _chunkSamples * sizeof(short);

        _chunk = new short[_chunkSamples];
        _chunkPin = GCHandle.Alloc(_chunk, GCHandleType.Pinned);
        _chunkPtr = _chunkPin.AddrOfPinnedObject();
        _processed = new float[_chunkSamples];

        // Never smaller than one input block, or a large block would immediately overflow.
        var capacity = Math.Max(_chunkSamples * _bufferedChunks, incomingSamples + _chunkSamples);
        _ring = new PcmRingBuffer(capacity);
        return true;
    }

    private void ReleaseChunk()
    {
        if (_chunkPin.IsAllocated) _chunkPin.Free();
        _chunkPtr = IntPtr.Zero;
        _chunk = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseChunk();
    }

    private static short FloatToS16(float v)
    {
        v *= 32768f;
        if (v > 32767f) v = 32767f;
        else if (v < -32768f) v = -32768f;
        return (short)(v + Math.Sign(v) * 0.5f);
    }
}
