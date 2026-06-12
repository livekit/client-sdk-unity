using System;
using System.Collections.Generic;

namespace LiveKit.Internal
{
    /// <summary>
    /// Pure logic for reading a looping microphone clip ring buffer whose position counter may be
    /// unreliable. Free of UnityEngine dependencies so it can be unit tested.
    /// </summary>
    /// <remarks>
    /// On most devices the position counter advances at the clip's data rate and capture is a
    /// plain contiguous read. On macOS with a Bluetooth HFP headset, however, FMOD writes each
    /// real packet of clip.frequency audio and then advances the counter k (~3.2) times too far,
    /// zero-filling the skipped range: the buffer holds valid fragments of N samples at a stride
    /// J (measured: 320 of every 1024), the fragments join continuously, and the counter rate is
    /// k = J/N times the data rate. The reader measures the counter rate and its smallest
    /// discrete jump during a pre-roll; when the rate is inflated it emits only the first J/k
    /// samples of each stride, reconstructing the contiguous stream.
    /// </remarks>
    internal sealed class MicClipReader
    {
        public struct ReadRange
        {
            public int Start;
            public int Count;
        }

        private readonly int _clipFrames;
        private readonly int _dataRate;
        private readonly double _preRollSeconds;
        private readonly double _fragmentedKThreshold;
        private readonly double _maxBacklogSeconds;
        private readonly double _settleSeconds;

        private bool _hasFirstSample;
        private int _prevCounter;
        private double _firstSampleTime;
        private double _measureStart = double.NaN;
        private long _preRollAdvance;
        private long _minJump = long.MaxValue;

        private long _maxBacklog;
        private int _readPos;
        private long _pending;

        /// <summary>False during the pre-roll measurement window; no ranges are emitted until ready.</summary>
        public bool Ready { get; private set; }

        /// <summary>True when the counter rate is inflated and only part of each stride holds data.</summary>
        public bool Fragmented { get; private set; }

        /// <summary>Counter samples per fragment cycle (0 when not fragmented).</summary>
        public int Stride { get; private set; }

        /// <summary>Valid data samples at the start of each stride (0 when not fragmented).</summary>
        public int ValidPerStride { get; private set; }

        /// <summary>Measured counter advance per second.</summary>
        public double CounterRate { get; private set; }

        /// <summary>Counter inflation factor: CounterRate / dataRate (~1 on healthy devices).</summary>
        public double K { get; private set; }

        /// <summary>Total counter samples discarded by backlog drops (e.g. after a stall).</summary>
        public long TotalDropped { get; private set; }

        public MicClipReader(int clipFrames, int dataRate,
            double preRollSeconds = 0.3, double fragmentedKThreshold = 1.5, double maxBacklogSeconds = 0.2,
            double settleSeconds = 0.1)
        {
            if (clipFrames <= 0) throw new ArgumentOutOfRangeException(nameof(clipFrames));
            if (dataRate <= 0) throw new ArgumentOutOfRangeException(nameof(dataRate));
            _clipFrames = clipFrames;
            _dataRate = dataRate;
            _preRollSeconds = preRollSeconds;
            _fragmentedKThreshold = fragmentedKThreshold;
            _maxBacklogSeconds = maxBacklogSeconds;
            _settleSeconds = settleSeconds;
        }

        /// <summary>
        /// Feeds the current counter position at a monotonic time and appends the ranges that
        /// should be read from the clip (already split at the ring wrap) to <paramref name="ranges"/>.
        /// </summary>
        public void Update(int counterPosition, double elapsedSeconds, List<ReadRange> ranges)
        {
            if (!_hasFirstSample)
            {
                _hasFirstSample = true;
                _prevCounter = counterPosition;
                _firstSampleTime = elapsedSeconds;
                return;
            }

            long d = ((counterPosition - _prevCounter) % _clipFrames + _clipFrames) % _clipFrames;
            _prevCounter = counterPosition;

            if (!Ready)
            {
                // Discard the settle window entirely: right after a device starts, the counter can
                // burst ahead while driver buffers flush, which would inflate the measured rate
                // (observed: a healthy device measuring k=1.07 right after a device transition).
                if (elapsedSeconds - _firstSampleTime < _settleSeconds)
                    return;
                if (double.IsNaN(_measureStart))
                {
                    // Anchor the measurement window here; the delta spanning the settle boundary
                    // is discarded with the settle period.
                    _measureStart = elapsedSeconds;
                    return;
                }

                _preRollAdvance += d;
                if (d > 0 && d < _minJump) _minJump = d;
                double window = elapsedSeconds - _measureStart;
                if (window >= _preRollSeconds)
                    FinishPreRoll(window);
                return;
            }

            _pending += d;

            // After a long stall, drop the oldest backlog instead of pushing a burst that would
            // overrun the consumer. (A stall longer than one counter lap aliases the unwrapped
            // advance and silently loses whole laps; this bound covers everything observable.)
            if (_pending > _maxBacklog)
            {
                long drop = _pending - _maxBacklog;
                if (Fragmented) drop -= drop % Stride; // preserve stride alignment
                if (drop > 0)
                {
                    _readPos = (int)((_readPos + drop) % _clipFrames);
                    _pending -= drop;
                    TotalDropped += drop;
                }
            }

            if (Fragmented)
            {
                while (_pending >= Stride)
                {
                    EmitSplit(_readPos, ValidPerStride, ranges);
                    _readPos = (_readPos + Stride) % _clipFrames;
                    _pending -= Stride;
                }
            }
            else if (_pending > 0)
            {
                EmitSplit(_readPos, (int)_pending, ranges);
                _readPos = (int)((_readPos + _pending) % _clipFrames);
                _pending = 0;
            }
        }

        private void FinishPreRoll(double windowSeconds)
        {
            CounterRate = _preRollAdvance > 0 ? _preRollAdvance / windowSeconds : _dataRate;
            K = CounterRate / _dataRate;
            Fragmented = K > _fragmentedKThreshold && _minJump != long.MaxValue && _minJump > 1;
            Stride = Fragmented ? (int)_minJump : 0;
            ValidPerStride = Fragmented ? Math.Max(1, (int)Math.Round(Stride / K)) : 0;
            _maxBacklog = (long)(CounterRate * _maxBacklogSeconds);
            _readPos = _prevCounter; // counter values land on jump boundaries
            _pending = 0;
            Ready = true;
        }

        private void EmitSplit(int start, int count, List<ReadRange> ranges)
        {
            if (count <= 0) return;
            int first = Math.Min(count, _clipFrames - start);
            ranges.Add(new ReadRange { Start = start, Count = first });
            if (count > first)
                ranges.Add(new ReadRange { Start = 0, Count = count - first });
        }
    }
}
