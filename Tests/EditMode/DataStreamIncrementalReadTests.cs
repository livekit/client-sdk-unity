using System;
using System.Collections.Generic;
using LiveKit;
using LiveKit.Internal;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class DataStreamIncrementalReadTests
    {
        private sealed class TestIncrementalReader : ReadIncrementalInstructionBase<string>
        {
            public TestIncrementalReader(FfiHandle h) : base(h) { }
            public void PushChunk(string content) => OnChunk(content);
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
    }
}
