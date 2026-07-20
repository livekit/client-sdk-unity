using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Internal;
using NUnit.Framework;

using LiveKit.Internal.FFI;
namespace LiveKit.EditModeTests
{
    public class DataStreamTests
    {
        private sealed class TestIncrementalReader : ReadIncrementalInstructionBase<string>
        {
            public TestIncrementalReader(FfiHandle h) : base(h) { }
            public void PushChunk(string content) => OnChunk(content);
            public void PushEos(LiveKit.Proto.StreamError error = null) => OnEos(error);
            public string Value => LatestChunk;
        }

        [Test]
        public void OnChunk_MultipleChunksBeforeConsumerDrains_AllChunksAreObserved()
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            // Three ChunkReceived events arrive back-to-back on the sync context,
            // before the consumer coroutine resumes from its yield.
            reader.PushChunk("A");
            reader.PushChunk("B");
            reader.PushChunk("C");

            // Drain like a consumer would: read the current chunk, Reset, loop
            // while more is readable.
            var observed = new List<string>();
            while (!reader.keepWaiting)
            {
                observed.Add(reader.Value);
                if (reader.IsEos) break;
                reader.Reset();
            }

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, observed,
                "ReadIncrementalInstructionBase dropped chunks when multiple " +
                "ChunkReceived events arrived before the consumer could yield.");
        }

        // Stream-reader chunk events now run on the FFI callback thread (no main-thread
        // marshal). The base class serializes its mutations under a lock; this test
        // exercises that path: a producer thread pushes chunks while the test thread
        // drains via the public Reset / LatestChunk API. All chunks must arrive in
        // FIFO order with no loss, no duplication, and no exception.
        [Test]
        public void OnChunk_ConcurrentProducerAndConsumer_AllChunksObservedInOrder()
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            const int total = 5_000;
            var producer = Task.Run(() =>
            {
                for (int i = 0; i < total; i++) reader.PushChunk(i.ToString());
            });

            var observed = new List<string>(total);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (observed.Count < total)
            {
                if (DateTime.UtcNow > deadline)
                    Assert.Fail($"Drain timed out at {observed.Count}/{total} chunks — likely deadlock or chunk loss.");

                if (reader.keepWaiting)
                {
                    Thread.Yield();
                    continue;
                }
                observed.Add(reader.Value);
                reader.Reset();
            }

            producer.Wait(TimeSpan.FromSeconds(5));

            Assert.AreEqual(total, observed.Count,
                "Producer pushed N chunks but consumer observed a different count.");
            for (int i = 0; i < total; i++)
                Assert.AreEqual(i.ToString(), observed[i], $"Chunk reordering at index {i}.");
        }

        // OnEos races with OnChunk on the FFI thread. EOS is an ordered item behind the
        // chunks, so after both complete the consumer must be able to drain every chunk in
        // FIFO order before observing end-of-stream — never seeing EOS while data remains.
        [Test]
        public void OnEos_RacingWithChunks_AllChunksDrainThenEos()
        {
            for (int trial = 0; trial < 50; trial++)
            {
                using var handle = new FfiHandle(IntPtr.Zero);
                var reader = new TestIncrementalReader(handle);

                var start = new ManualResetEventSlim(false);
                var producer = Task.Run(() =>
                {
                    start.Wait();
                    for (int i = 0; i < 100; i++) reader.PushChunk(i.ToString());
                    reader.PushEos();
                });

                start.Set();
                producer.Wait(TimeSpan.FromSeconds(5));

                var observed = new List<string>(100);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (true)
                {
                    while (reader.keepWaiting)
                    {
                        if (DateTime.UtcNow > deadline)
                            Assert.Fail($"Trial {trial}: drain stalled at {observed.Count}/100 — chunk lost or deadlock.");
                        Thread.Yield();
                    }
                    if (reader.IsEos) break;
                    observed.Add(reader.Value);
                    reader.Reset();
                }

                Assert.AreEqual(100, observed.Count,
                    $"Trial {trial}: EOS observed before all chunks were drained.");
                for (int i = 0; i < 100; i++)
                    Assert.AreEqual(i.ToString(), observed[i], $"Trial {trial}: chunk reordering at index {i}.");

                // A clean end-of-stream leaves no chunk to read: LatestChunk returns the
                // default value and never throws (a torn read would surface here).
                Assert.DoesNotThrow(() => { var _ = reader.Value; },
                    $"Trial {trial}: reading LatestChunk after EOS race threw unexpectedly.");
                Assert.IsNull(reader.Value, $"Trial {trial}: expected null chunk at end-of-stream.");
            }
        }

        // The whole stream — every chunk AND end-of-stream — arrives before the consumer
        // reads anything (the common small-stream / already-buffered case). Every chunk must
        // still be delivered before EOS is observed. This is the core regression the
        // single-queue / EOS-as-terminal-item fix addresses: previously IsEos short-circuited
        // the buffered chunks and the whole stream was dropped.
        [Test]
        public void Burst_AllChunksThenEosBeforeConsumer_AllDrainedThenEos()
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            reader.PushChunk("A");
            reader.PushChunk("B");
            reader.PushChunk("C");
            reader.PushEos();

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Drain(reader));
            Assert.IsTrue(reader.IsEos);
        }

        // A single chunk followed immediately by end-of-stream — the previous implementation
        // dropped the chunk because IsEos short-circuited the read on the first iteration.
        [Test]
        public void Burst_SingleChunkThenEos_ChunkDelivered()
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            reader.PushChunk("only");
            reader.PushEos();

            CollectionAssert.AreEqual(new[] { "only" }, Drain(reader));
            Assert.IsTrue(reader.IsEos);
        }

        // An empty stream (end-of-stream with no chunks) drains to zero chunks and reports EOS.
        [Test]
        public void Burst_EosWithNoChunks_ReportsEosWithNoChunks()
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            reader.PushEos();

            CollectionAssert.IsEmpty(Drain(reader));
            Assert.IsTrue(reader.IsEos);
        }

        // Chunks that precede an error close are still delivered in order; the error then
        // surfaces via IsError/Error once the consumer reaches the terminal marker.
        [Test]
        public void Burst_ChunksThenErrorEos_ChunksDrainedThenErrorSurfaces()
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            reader.PushChunk("A");
            reader.PushChunk("B");
            reader.PushEos(new LiveKit.Proto.StreamError { Description = "boom" });

            // Read-first drain; break on EOS before reading (an error terminal has no chunk,
            // and reading it would rethrow the StreamError).
            var observed = new List<string>();
            while (!reader.keepWaiting)
            {
                if (reader.IsEos) break;
                observed.Add(reader.Value);
                reader.Reset();
            }

            CollectionAssert.AreEqual(new[] { "A", "B" }, observed);
            Assert.IsTrue(reader.IsEos);
            Assert.IsTrue(reader.IsError);
            Assert.AreEqual("boom", reader.Error.Message);
        }

        // Canonical read-first drain used by the tests above: spin until the reader is
        // positioned on a chunk or the terminal marker, break on EOS, else read and advance.
        private static List<string> Drain(TestIncrementalReader reader)
        {
            var observed = new List<string>();
            while (!reader.keepWaiting)
            {
                if (reader.IsEos) break;
                observed.Add(reader.Value);
                reader.Reset();
            }
            return observed;
        }

        // OnChunk-after-Reset is the most common interleaving in production: the
        // consumer's coroutine just yielded and called Reset, and an FFI thread
        // pushes a chunk before the consumer wakes. The lock must serialize them
        // so the new chunk is correctly placed (either as the current item if the
        // queue was empty, or appended to the queue).
        [Test]
        public void OnChunk_RacingWithReset_NoChunkLost()
        {
            for (int trial = 0; trial < 200; trial++)
            {
                using var handle = new FfiHandle(IntPtr.Zero);
                var reader = new TestIncrementalReader(handle);

                // Prime: one chunk so the consumer can do a first read+Reset.
                reader.PushChunk("seed");

                var observed = new List<string>();
                observed.Add(reader.Value);

                // Race: the next chunk push happens concurrently with Reset.
                var pushTask = Task.Run(() => reader.PushChunk("racer"));
                reader.Reset();
                if (!pushTask.Wait(TimeSpan.FromSeconds(2)))
                    Assert.Fail($"Trial {trial}: producer-side OnChunk did not return within 2s — likely lock deadlock.");

                // After both operations, draining must yield "racer" exactly once.
                var deadline = DateTime.UtcNow.AddMilliseconds(500);
                while (reader.keepWaiting)
                {
                    if (DateTime.UtcNow > deadline)
                        Assert.Fail($"Trial {trial}: 'racer' chunk not observable after race.");
                    Thread.Yield();
                }
                observed.Add(reader.Value);

                CollectionAssert.AreEqual(new[] { "seed", "racer" }, observed,
                    $"Trial {trial}: chunk order or content corrupted by Reset/OnChunk race.");
            }
        }
    }
}
