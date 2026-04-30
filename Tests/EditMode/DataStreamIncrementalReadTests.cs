using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiveKit.Internal;
using NUnit.Framework;

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
        [Test, Timeout(15_000)]
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

        // OnEos races with OnChunk on the FFI thread. After both complete, the lock
        // must leave the instruction in a consistent state: IsEos visible, no torn
        // reads of LatestChunk, and the chunks pushed before EOS still drainable
        // (up to the point where Reset() is correctly disallowed past EOS).
        [Test, Timeout(10_000)]
        public void OnEos_RacingWithChunks_FinalStateConsistent()
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

                Assert.IsTrue(reader.IsEos, $"Trial {trial}: EOS not observed after producer finished.");
                // Reading LatestChunk after EOS must never throw an unexpected exception
                // — the only allowed throw is the StreamError on protocol error, which
                // we don't simulate here. Specifically: no NullReferenceException, no
                // collection-modified exception, no torn-read.
                Assert.DoesNotThrow(() => { var _ = reader.Value; },
                    $"Trial {trial}: reading LatestChunk after EOS race threw unexpectedly.");
            }
        }

        // OnChunk-after-Reset is the most common interleaving in production: the
        // consumer's coroutine just yielded and called Reset, and an FFI thread
        // pushes a chunk before the consumer wakes. The lock must serialize them
        // so the new chunk is correctly placed (either as _latestChunk if the
        // queue was empty, or appended to the queue).
        [Test, Timeout(5_000)]
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
                pushTask.Wait();

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
