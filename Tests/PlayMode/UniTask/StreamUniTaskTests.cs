#if LIVEKIT_UNITASK
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiveKit.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests.UniTaskBridge
{
    public class StreamUniTaskTests
    {
        // Synthetic incremental reader that drives the base chunk/EoS machinery directly,
        // with no FFI — the same seam used by the EditMode DataStreamIncrementalReadTests.
        // FfiHandle is public; new FfiHandle(IntPtr.Zero) is a valid dummy handle.
        private sealed class TestIncrementalReader : ReadIncrementalInstructionBase<string>
        {
            public TestIncrementalReader(FfiHandle h) : base(h) { }
            public void PushChunk(string content) => OnChunk(content);
            public void PushEos(LiveKit.Proto.StreamError error = null) => OnEos(error);
        }

        // Chunks pushed and consumed one at a time arrive in order; the sequence ends when
        // EoS is observed. Manual enumeration interleaves push/pull so EoS only follows a
        // fully drained queue (matching how chunks arrive over time in production).
        [UnityTest]
        public System.Collections.IEnumerator AsAsyncEnumerable_DeliversChunksInOrder_ThenStops() => UniTask.ToCoroutine(async () =>
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            var e = reader.AsAsyncEnumerable().GetAsyncEnumerator();
            try
            {
                reader.PushChunk("A");
                Assert.IsTrue(await e.MoveNextAsync(), "Expected chunk A");
                Assert.AreEqual("A", e.Current);

                reader.PushChunk("B");
                Assert.IsTrue(await e.MoveNextAsync(), "Expected chunk B");
                Assert.AreEqual("B", e.Current);

                reader.PushChunk("C");
                Assert.IsTrue(await e.MoveNextAsync(), "Expected chunk C");
                Assert.AreEqual("C", e.Current);

                reader.PushEos();
                Assert.IsFalse(await e.MoveNextAsync(), "Enumeration must end at EoS");
            }
            finally
            {
                await e.DisposeAsync();
            }
        });

        // The current chunk is delivered even when EoS is already set at the time it is read,
        // then the sequence ends. (Chunks buffered beyond the current one when EoS arrives are
        // not drainable — a pre-existing reader limitation, asserted here for clarity.)
        [UnityTest]
        public System.Collections.IEnumerator AsAsyncEnumerable_DeliversFinalChunkThenEos() => UniTask.ToCoroutine(async () =>
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            reader.PushChunk("only");
            reader.PushEos();

            var observed = new List<string>();
            await foreach (var chunk in reader.AsAsyncEnumerable())
                observed.Add(chunk);

            CollectionAssert.AreEqual(new[] { "only" }, observed);
        });

        // A chunk delivered before the stream errors is observed; the subsequent error EoS
        // then surfaces as a thrown StreamError. Manual enumeration models the real timeline
        // (chunk arrives, is consumed, THEN the error ends the stream) — note that once the
        // error is set, LatestChunk itself throws, so the error must follow chunk delivery.
        [UnityTest]
        public System.Collections.IEnumerator AsAsyncEnumerable_ThrowsStreamError_AfterDeliveringChunk() => UniTask.ToCoroutine(async () =>
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);

            var e = reader.AsAsyncEnumerable().GetAsyncEnumerator();
            try
            {
                reader.PushChunk("partial");
                Assert.IsTrue(await e.MoveNextAsync(), "Expected the pre-error chunk");
                Assert.AreEqual("partial", e.Current);

                reader.PushEos(new LiveKit.Proto.StreamError { Description = "boom" });

                StreamError caught = null;
                try
                {
                    await e.MoveNextAsync();
                }
                catch (StreamError ex)
                {
                    caught = ex;
                }

                Assert.IsNotNull(caught, "Expected the error EoS to throw a StreamError");
                Assert.AreEqual("boom", caught.Message);
            }
            finally
            {
                await e.DisposeAsync();
            }
        });

        // A cancelled token surfaces as OperationCanceledException with abandon-awaiter
        // semantics: nothing is observed and the underlying reader is untouched.
        [UnityTest]
        public System.Collections.IEnumerator AsAsyncEnumerable_Cancellation_ThrowsOperationCanceled() => UniTask.ToCoroutine(async () =>
        {
            using var handle = new FfiHandle(IntPtr.Zero);
            var reader = new TestIncrementalReader(handle);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var observed = new List<string>();
            bool threw = false;
            try
            {
                await foreach (var chunk in reader.AsAsyncEnumerable(cts.Token))
                    observed.Add(chunk);
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "Expected OperationCanceledException for a cancelled token");
            CollectionAssert.IsEmpty(observed);
            Assert.IsFalse(reader.IsEos, "Abandon-awaiter semantics: reader state is untouched");
        });
    }
}
#endif
