using System;
using System.Collections.Generic;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class RawSafeDispatchTests
    {
        // Captures Post calls so a test can verify FFIClient sent work to the
        // main-thread sync context for non-raw entries, and (optionally) drain
        // the queued lambda synchronously to verify the completion would have
        // run on the main thread.
        private sealed class RecordingSyncContext : SynchronizationContext
        {
            public readonly List<(SendOrPostCallback callback, object state)> Posts = new();
            public override void Post(SendOrPostCallback d, object state)
            {
                Posts.Add((d, state));
            }
            public void DrainOnce()
            {
                foreach (var (cb, st) in Posts) cb(st);
                Posts.Clear();
            }
        }

        // Each test draws a fresh asyncId from this counter so concurrent tests
        // don't collide in FfiClient.Instance's shared pendingCallbacks map. The
        // high-bit seed avoids overlap with real request ids that the SDK might
        // generate during test setup of unrelated fixtures.
        private static long _asyncIdSeed = 0x7FF0_0000_0000_0000L;
        private static ulong NextAsyncId() => (ulong)Interlocked.Increment(ref _asyncIdSeed);

        [Test]
        public void RawSafeTrueCallback_RunsOnDispatchingBackgroundThread()
        {
            var asyncId = NextAsyncId();
            var testThreadId = Thread.CurrentThread.ManagedThreadId;
            int completionThreadId = -1;
            var done = new ManualResetEventSlim(false);

            FfiClient.Instance.RegisterPendingCallback<UnpublishTrackCallback>(
                asyncId,
                static e => e.UnpublishTrack,
                cb =>
                {
                    completionThreadId = Thread.CurrentThread.ManagedThreadId;
                    done.Set();
                },
                rawSafe: true);

            int dispatchThreadId = -1;
            // Use an explicit Thread (not Task.Run) so we know the dispatcher is
            // a fresh OS thread that the runtime has no reason to alias to the
            // test thread. A thread-pool worker happens to differ in practice but
            // is not guaranteed to.
            var dispatcher = new Thread(() =>
            {
                dispatchThreadId = Thread.CurrentThread.ManagedThreadId;
                var ev = new FfiEvent
                {
                    UnpublishTrack = new UnpublishTrackCallback { AsyncId = asyncId }
                };
                FfiClient.Instance.TryDispatchRawSafe(asyncId, ev);
            });
            dispatcher.Start();
            Assert.IsTrue(dispatcher.Join(TimeSpan.FromSeconds(2)),
                "Dispatcher thread did not finish within 2s.");

            Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(1)),
                "Completion did not run within 1s — TryDispatchRawSafe may have failed silently.");
            Assert.AreEqual(dispatchThreadId, completionThreadId,
                "rawSafe completion did not run on the dispatching thread — the FFI-thread fast path is not being taken.");
            Assert.AreNotEqual(testThreadId, completionThreadId,
                "rawSafe completion ran on the test main thread — it was marshalled rather than dispatched raw.");
        }

        [Test]
        public void RawSafeFalseCallback_TryDispatchRawSafe_ReturnsFalseAndDoesNotComplete()
        {
            var asyncId = NextAsyncId();
            var completionRan = false;

            FfiClient.Instance.RegisterPendingCallback<UnpublishTrackCallback>(
                asyncId,
                static e => e.UnpublishTrack,
                cb => { completionRan = true; },
                rawSafe: false);

            try
            {
                var ev = new FfiEvent
                {
                    UnpublishTrack = new UnpublishTrackCallback { AsyncId = asyncId }
                };
                var dispatched = FfiClient.Instance.TryDispatchRawSafe(asyncId, ev);

                Assert.IsFalse(dispatched,
                    "TryDispatchRawSafe should return false for entries registered with rawSafe: false.");
                Assert.IsFalse(completionRan,
                    "Completion ran via TryDispatchRawSafe even though rawSafe was false.");
            }
            finally
            {
                // Clean up: the pending entry would otherwise leak into other tests.
                FfiClient.Instance.CancelPendingCallback(asyncId);
            }
        }

        // Integration test: drives the same code path FFICallback uses, so this
        // fails if a future refactor removes the rawSafe short-circuit from
        // RouteFfiEvent. Calling RouteFfiEvent from a fresh Thread simulates
        // Rust calling FFICallback from its own worker thread.
        [Test]
        public void RouteFfiEvent_RawSafeTrue_CompletesOnDispatchingThread()
        {
            var asyncId = NextAsyncId();
            var testThreadId = Thread.CurrentThread.ManagedThreadId;
            int completionThreadId = -1;
            var done = new ManualResetEventSlim(false);

            FfiClient.Instance.RegisterPendingCallback<UnpublishTrackCallback>(
                asyncId,
                static e => e.UnpublishTrack,
                cb =>
                {
                    completionThreadId = Thread.CurrentThread.ManagedThreadId;
                    done.Set();
                },
                rawSafe: true);

            int dispatchThreadId = -1;
            var dispatcher = new Thread(() =>
            {
                dispatchThreadId = Thread.CurrentThread.ManagedThreadId;
                var ev = new FfiEvent
                {
                    UnpublishTrack = new UnpublishTrackCallback { AsyncId = asyncId }
                };
                FfiClient.RouteFfiEvent(ev);
            });
            dispatcher.Start();
            Assert.IsTrue(dispatcher.Join(TimeSpan.FromSeconds(2)),
                "Dispatcher thread did not finish within 2s.");

            Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(1)),
                "Completion did not run — RouteFfiEvent may be missing the TryDispatchRawSafe call for rawSafe entries.");
            Assert.AreEqual(dispatchThreadId, completionThreadId,
                "rawSafe completion did not run on the dispatching thread — RouteFfiEvent marshalled it instead.");
            Assert.AreNotEqual(testThreadId, completionThreadId,
                "rawSafe completion ran on the test main thread.");
        }

        // Integration test: non-raw entries must reach the main-thread
        // SynchronizationContext via Post. We swap _context for a recording
        // SC so we can observe the post (and drain it to verify the queued
        // lambda actually invokes the completion).
        [Test]
        public void RouteFfiEvent_RawSafeFalse_PostsToSynchronizationContext_AndDrainsCompletion()
        {
            var asyncId = NextAsyncId();
            int completionThreadId = -1;
            var completionRan = new ManualResetEventSlim(false);

            FfiClient.Instance.RegisterPendingCallback<UnpublishTrackCallback>(
                asyncId,
                static e => e.UnpublishTrack,
                cb =>
                {
                    completionThreadId = Thread.CurrentThread.ManagedThreadId;
                    completionRan.Set();
                },
                rawSafe: false);

            var recording = new RecordingSyncContext();
            var originalContext = FfiClient.Instance._context;
            FfiClient.Instance._context = recording;
            try
            {
                var ev = new FfiEvent
                {
                    UnpublishTrack = new UnpublishTrackCallback { AsyncId = asyncId }
                };

                int dispatchThreadId = -1;
                var dispatcher = new Thread(() =>
                {
                    dispatchThreadId = Thread.CurrentThread.ManagedThreadId;
                    FfiClient.RouteFfiEvent(ev);
                });
                dispatcher.Start();
                Assert.IsTrue(dispatcher.Join(TimeSpan.FromSeconds(2)),
                    "Dispatcher thread did not finish within 2s.");

                Assert.IsFalse(completionRan.IsSet,
                    "Non-raw completion ran synchronously on the dispatcher thread — it should have been posted, not executed.");
                Assert.AreEqual(1, recording.Posts.Count,
                    "RouteFfiEvent should have posted exactly one work item to the sync context for a non-raw pending entry.");

                // Drain the queued lambda on this (test) thread, simulating Unity's
                // main-thread queue drain. The completion must now run, and on this thread.
                var drainerThreadId = Thread.CurrentThread.ManagedThreadId;
                recording.DrainOnce();

                Assert.IsTrue(completionRan.Wait(TimeSpan.FromSeconds(1)),
                    "Drained queued post did not invoke the completion.");
                Assert.AreEqual(drainerThreadId, completionThreadId,
                    "Drained completion should run on the thread that drains the sync context.");
                Assert.AreNotEqual(dispatchThreadId, completionThreadId,
                    "Completion ran on the dispatcher's thread despite rawSafe being false.");
            }
            finally
            {
                FfiClient.Instance._context = originalContext;
                FfiClient.Instance.CancelPendingCallback(asyncId);
            }
        }

        [Test]
        public void TryDispatchPendingCallback_RunsCompletionSynchronouslyOnCallerThread()
        {
            // Sanity check: TryDispatchPendingCallback itself is synchronous on the
            // caller's thread regardless of rawSafe. What makes the rawSafe path
            // "raw" is that FFICallback (the FFI thread) is the caller, vs. the
            // SynchronizationContext-posted lambda (the main thread) for non-raw.
            var asyncId = NextAsyncId();
            int completionThreadId = -1;
            var done = new ManualResetEventSlim(false);

            FfiClient.Instance.RegisterPendingCallback<UnpublishTrackCallback>(
                asyncId,
                static e => e.UnpublishTrack,
                cb =>
                {
                    completionThreadId = Thread.CurrentThread.ManagedThreadId;
                    done.Set();
                },
                rawSafe: false);

            int dispatchThreadId = -1;
            var dispatcher = new Thread(() =>
            {
                dispatchThreadId = Thread.CurrentThread.ManagedThreadId;
                var ev = new FfiEvent
                {
                    UnpublishTrack = new UnpublishTrackCallback { AsyncId = asyncId }
                };
                FfiClient.Instance.TryDispatchPendingCallback(asyncId, ev);
            });
            dispatcher.Start();
            Assert.IsTrue(dispatcher.Join(TimeSpan.FromSeconds(2)),
                "Dispatcher thread did not finish within 2s.");

            Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(1)),
                "Completion did not run.");
            Assert.AreEqual(dispatchThreadId, completionThreadId,
                "TryDispatchPendingCallback did not run the completion synchronously on the caller's thread.");
        }
    }
}
