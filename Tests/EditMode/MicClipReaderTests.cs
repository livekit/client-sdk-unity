using System;
using System.Collections.Generic;
using NUnit.Framework;
using LiveKit.Internal;

namespace LiveKit.EditModeTests
{
    /// <summary>
    /// Tests for the microphone clip reading logic, including reconstruction of the fragmented
    /// buffers produced by macOS with Bluetooth HFP headsets (valid fragments of 320 samples at a
    /// 1024-sample stride with zero padding, position counter inflated k=3.2x; structure taken
    /// from a raw buffer dump of a Sony MDR-1000X).
    /// </summary>
    public class MicClipReaderTests
    {
        const double PreRoll = 0.3;

        static List<MicClipReader.ReadRange> Drain(MicClipReader reader, int counter, double t)
        {
            var ranges = new List<MicClipReader.ReadRange>();
            reader.Update(counter, t, ranges);
            return ranges;
        }

        // Runs the pre-roll with the given advance per tick, returning (counter, time) at the end.
        static (int counter, double t) RunPreRoll(MicClipReader reader, int clipFrames, int advancePerTick, double dt)
        {
            int counter = 0;
            double t = 0;
            reader.Update(counter, t, new List<MicClipReader.ReadRange>());
            while (!reader.Ready)
            {
                t += dt;
                counter = (counter + advancePerTick) % clipFrames;
                reader.Update(counter, t, new List<MicClipReader.ReadRange>());
            }
            return (counter, t);
        }

        [Test]
        public void HealthyDevice_UsesContiguousMode_AndEmitsAllSamples()
        {
            const int clipFrames = 96000; // 2s @ 48k
            const int rate = 48000;
            const int perTick = 480;      // 10ms ticks at the data rate
            const double dt = 0.01;

            var reader = new MicClipReader(clipFrames, rate, PreRoll);
            var (counter, t) = RunPreRoll(reader, clipFrames, perTick, dt);

            Assert.IsFalse(reader.Fragmented);
            Assert.AreEqual(1.0, reader.K, 0.02);

            long emitted = 0;
            for (int i = 0; i < 100; i++)
            {
                t += dt;
                counter = (counter + perTick) % clipFrames;
                foreach (var r in Drain(reader, counter, t))
                {
                    Assert.LessOrEqual(r.Start + r.Count, clipFrames, "range must not cross the ring wrap");
                    emitted += r.Count;
                }
            }
            Assert.AreEqual(100L * perTick, emitted, "contiguous mode must emit every written sample");
        }

        [Test]
        public void FragmentedDevice_DetectsStrideAndValidCount()
        {
            const int clipFrames = 32000; // 2s @ 16k
            const int rate = 16000;
            const int stride = 1024;      // one counter jump per real 20ms packet
            const double dt = 0.02;

            var reader = new MicClipReader(clipFrames, rate, PreRoll);
            RunPreRoll(reader, clipFrames, stride, dt);

            Assert.IsTrue(reader.Fragmented);
            Assert.AreEqual(3.2, reader.K, 0.05);
            Assert.AreEqual(stride, reader.Stride);
            Assert.AreEqual(320, reader.ValidPerStride);
        }

        [Test]
        public void FragmentedDevice_ReconstructsContiguousStream()
        {
            const int clipFrames = 32000;
            const int rate = 16000;
            const int stride = 1024;
            const int valid = 320;
            const double dt = 0.02;

            var reader = new MicClipReader(clipFrames, rate, PreRoll);

            // Simulated clip: each tick the writer stores `valid` sequential marker values at the
            // counter's previous position and zero-fills the rest of the stride, exactly like the
            // dumped MDR-1000X buffer.
            var clip = new float[clipFrames];
            float marker = 1f;
            int counter = 0;
            double t = 0;
            reader.Update(counter, t, new List<MicClipReader.ReadRange>());

            void WriteFragment()
            {
                for (int i = 0; i < stride; i++)
                    clip[(counter + i) % clipFrames] = i < valid ? marker + i : 0f;
                marker += valid;
                counter = (counter + stride) % clipFrames;
            }

            while (!reader.Ready)
            {
                t += dt;
                WriteFragment();
                reader.Update(counter, t, new List<MicClipReader.ReadRange>());
            }

            // Capture for several buffer laps and verify the emitted stream is the unbroken
            // marker sequence: lossless reconstruction with no gaps, repeats, or padding.
            var collected = new List<float>();
            for (int tick = 0; tick < 200; tick++)
            {
                t += dt;
                WriteFragment();
                foreach (var r in Drain(reader, counter, t))
                {
                    Assert.LessOrEqual(r.Start + r.Count, clipFrames, "range must not cross the ring wrap");
                    for (int i = 0; i < r.Count; i++)
                        collected.Add(clip[r.Start + i]);
                }
            }

            Assert.AreEqual(200 * valid, collected.Count, "every valid fragment must be emitted exactly once");
            for (int i = 1; i < collected.Count; i++)
                Assert.AreEqual(collected[i - 1] + 1f, collected[i], $"stream must be contiguous at index {i}");
        }

        [Test]
        public void FragmentedDevice_DropsStaleBacklogStrideAligned()
        {
            const int clipFrames = 32000;
            const int rate = 16000;
            const int stride = 1024;
            const double dt = 0.02;
            const double maxBacklogSec = 0.2;

            var reader = new MicClipReader(clipFrames, rate, PreRoll, 1.05, maxBacklogSec);
            var (counter, t) = RunPreRoll(reader, clipFrames, stride, dt);

            // One giant advance (a main-thread stall): 25 strides at once.
            const int stalledStrides = 25;
            counter = (counter + stalledStrides * stride) % clipFrames;
            t += stalledStrides * dt;
            var ranges = Drain(reader, counter, t);

            Assert.Greater(reader.TotalDropped, 0, "stall backlog must be dropped");
            Assert.AreEqual(0, reader.TotalDropped % stride, "drop must preserve stride alignment");

            // Emitted + dropped must account for the whole advance (in counter units).
            long emittedStrides = 0;
            foreach (var r in ranges) emittedStrides += r.Count;
            emittedStrides /= reader.ValidPerStride;
            Assert.AreEqual(stalledStrides, emittedStrides + reader.TotalDropped / stride);

            // The bounded burst must not exceed the backlog limit.
            Assert.LessOrEqual(emittedStrides * stride, (long)(reader.CounterRate * maxBacklogSec));
        }

        [Test]
        public void SlightlyInflatedCounter_StaysContiguous()
        {
            // Regression: a healthy MacBook mic measured k=1.07 right after a device transition
            // (startup-burst noise), and the old 1.05 threshold engaged fragmented mode, silently
            // discarding ~6% of real audio. Borderline rates must stay contiguous.
            const int clipFrames = 96000;
            const int rate = 48000;
            const int perTick = 514; // ~k=1.07 at 10ms ticks
            const double dt = 0.01;

            var reader = new MicClipReader(clipFrames, rate, PreRoll);
            RunPreRoll(reader, clipFrames, perTick, dt);

            Assert.IsFalse(reader.Fragmented, "k slightly above 1 must not trigger fragmented mode");
            Assert.AreEqual(1.07, reader.K, 0.02);
        }

        [Test]
        public void NoRangesAreEmittedDuringPreRoll()
        {
            const int clipFrames = 96000;
            var reader = new MicClipReader(clipFrames, 48000, PreRoll);
            var ranges = new List<MicClipReader.ReadRange>();
            reader.Update(0, 0.0, ranges);
            reader.Update(480, 0.01, ranges);
            reader.Update(960, 0.02, ranges);
            Assert.IsFalse(reader.Ready);
            Assert.IsEmpty(ranges);
        }
    }

    public class StreamingResamplerTests
    {
        static float[] Sine(int count, double freqHz, int rate)
        {
            var s = new float[count];
            for (int i = 0; i < count; i++)
                s[i] = (float)Math.Sin(2.0 * Math.PI * freqHz * i / rate);
            return s;
        }

        static int ZeroCrossings(IReadOnlyList<float> s)
        {
            int n = 0;
            for (int i = 1; i < s.Count; i++)
                if ((s[i - 1] < 0f) != (s[i] < 0f)) n++;
            return n;
        }

        [Test]
        public void Upsample16kTo48k_PreservesFrequencyAndLength()
        {
            const int inRate = 16000, outRate = 48000;
            var input = Sine(16000, 200.0, inRate); // 1s of 200Hz
            var resampler = new StreamingResampler(inRate, outRate);
            var output = resampler.Process(input, input.Length);

            Assert.AreEqual(outRate, output.Length, outRate / 100, "1s in should be ~1s out at the new rate");
            // 200Hz over ~1s crosses zero ~400 times regardless of sample rate.
            Assert.AreEqual(ZeroCrossings(input), ZeroCrossings(output), 4);
        }

        [Test]
        public void ChunkedProcessing_MatchesWholeProcessing()
        {
            const int inRate = 16000, outRate = 48000;
            var input = Sine(3200, 250.0, inRate);

            var whole = new StreamingResampler(inRate, outRate).Process(input, input.Length);

            // Process the same stream in 320-sample fragments (the MDR-1000X packet size).
            var chunked = new List<float>();
            var resampler = new StreamingResampler(inRate, outRate);
            for (int off = 0; off < input.Length; off += 320)
            {
                var chunk = new float[320];
                Array.Copy(input, off, chunk, 0, 320);
                chunked.AddRange(resampler.Process(chunk, 320));
            }

            // Accumulated floating-point rounding differs by an ulp between the two paths (the
            // chunked position is renormalized per chunk), which can flip the final boundary
            // sample — allow a 1-sample tail difference, but the overlap must match exactly.
            Assert.AreEqual(whole.Length, chunked.Count, 1, "chunking must not change the output length (±1 tail sample)");
            int overlap = Math.Min(whole.Length, chunked.Count);
            for (int i = 0; i < overlap; i++)
                Assert.AreEqual(whole[i], chunked[i], 1e-4f, $"chunked output diverges at {i}");
        }
    }
}
