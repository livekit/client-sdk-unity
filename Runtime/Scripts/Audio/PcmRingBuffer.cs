using System;

namespace LiveKit
{
    /// <summary>
    /// Fixed-capacity interleaved int16 PCM ring buffer with a fixed-size drain. Re-chunks Unity's
    /// DSP-block-sized audio into the 10 ms frames the <see cref="AudioProcessingModule"/> requires.
    /// </summary>
    /// <remarks>
    /// Allocation-free after construction: both users run on the Unity audio thread. Sized in
    /// samples (frames × channels), not frames. Single producer and single consumer per instance;
    /// the capture and reference feeds each own one, so there is no synchronisation inside.
    /// </remarks>
    internal sealed class PcmRingBuffer
    {
        private readonly short[] _buffer;
        private int _readIndex;
        private int _writeIndex;
        private int _count;

        /// <summary>Samples dropped because the buffer was full, since construction.</summary>
        public int OverflowSamples { get; private set; }

        public int Capacity => _buffer.Length;
        public int Available => _count;

        public PcmRingBuffer(int capacitySamples)
        {
            if (capacitySamples <= 0) throw new ArgumentOutOfRangeException(nameof(capacitySamples));
            _buffer = new short[capacitySamples];
        }

        /// <summary>
        /// Appends <paramref name="count"/> samples. When the buffer is full the OLDEST samples are
        /// dropped: a stalled consumer must not push the echo reference arbitrarily far out of
        /// alignment with the capture stream.
        /// </summary>
        public void Write(short[] source, int offset, int count)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (offset < 0 || count < 0 || offset + count > source.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count >= _buffer.Length)
            {
                OverflowSamples += _count + count - _buffer.Length;
                offset += count - _buffer.Length;
                count = _buffer.Length;
                _readIndex = 0;
                _writeIndex = 0;
                _count = 0;
            }
            else
            {
                var free = _buffer.Length - _count;
                if (count > free) Discard(count - free);
            }

            for (var i = 0; i < count; i++)
            {
                _buffer[_writeIndex] = source[offset + i];
                _writeIndex = _writeIndex + 1 == _buffer.Length ? 0 : _writeIndex + 1;
            }

            _count += count;
        }

        /// <summary>
        /// Copies exactly <paramref name="count"/> samples into <paramref name="destination"/> and
        /// consumes them. Returns false and consumes nothing when fewer are available.
        /// </summary>
        public bool TryDrain(short[] destination, int count)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (count < 0 || count > destination.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (_count < count) return false;

            for (var i = 0; i < count; i++)
            {
                destination[i] = _buffer[_readIndex];
                _readIndex = _readIndex + 1 == _buffer.Length ? 0 : _readIndex + 1;
            }

            _count -= count;
            return true;
        }

        public void Clear()
        {
            _readIndex = 0;
            _writeIndex = 0;
            _count = 0;
        }

        private void Discard(int count)
        {
            if (count > _count) count = _count;
            _readIndex = (_readIndex + count) % _buffer.Length;
            _count -= count;
            OverflowSamples += count;
        }
    }

    /// <summary>Sample format conversions shared by the capture paths.</summary>
    internal static class PcmConvert
    {
        /// <summary>Float [-1, 1] to int16 with clamping and round-half-away-from-zero.</summary>
        public static short FloatToS16(float v)
        {
            v *= 32768f;
            if (v > 32767f) v = 32767f;
            else if (v < -32768f) v = -32768f;
            return (short)(v + Math.Sign(v) * 0.5f);
        }

        public static float S16ToFloat(short v) => v / 32768f;
    }
}
