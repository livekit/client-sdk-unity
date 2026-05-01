using System;
using System.Collections.Generic;
using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class SkipDispatchTests
    {
        // Captures Post calls so a test can verify FFIClient sent work to the
        // main-thread sync context for entries registered with
        // dispatchToMainThread:true, and (optionally) drain the queued lambda
        // synchronously to verify the completion would have run on the main thread.
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
        public void InlineCallback_RunsOnDispatchingBackgroundThread()
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
                dispatchToMainThread: false);

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
                FfiClient.Instance.TrySkipDispatch(asyncId, ev);
            });
            dispatcher.Start();
            Assert.IsTrue(dispatcher.Join(TimeSpan.FromSeconds(2)),
                "Dispatcher thread did not finish within 2s.");

            Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(1)),
                "Completion did not run within 1s — TrySkipDispatch may have failed silently.");
            Assert.AreEqual(dispatchThreadId, completionThreadId,
                "Inline completion did not run on the dispatching thread — the FFI-thread fast path is not being taken.");
            Assert.AreNotEqual(testThreadId, completionThreadId,
                "Inline completion ran on the test main thread — it was marshalled rather than completed inline.");
        }

        [Test]
        public void MainThreadCallback_TrySkipDispatch_ReturnsFalseAndDoesNotComplete()
        {
            var asyncId = NextAsyncId();
            var completionRan = false;

            FfiClient.Instance.RegisterPendingCallback<UnpublishTrackCallback>(
                asyncId,
                static e => e.UnpublishTrack,
                cb => { completionRan = true; },
                dispatchToMainThread: true);

            try
            {
                var ev = new FfiEvent
                {
                    UnpublishTrack = new UnpublishTrackCallback { AsyncId = asyncId }
                };
                var skipped = FfiClient.Instance.TrySkipDispatch(asyncId, ev);

                Assert.IsFalse(skipped,
                    "TrySkipDispatch should return false for entries registered with dispatchToMainThread:true.");
                Assert.IsFalse(completionRan,
                    "Completion ran via TrySkipDispatch even though the entry requires main-thread dispatch.");
            }
            finally
            {
                // Clean up: the pending entry would otherwise leak into other tests.
                FfiClient.Instance.CancelPendingCallback(asyncId);
            }
        }

        // Integration test: drives the same code path FFICallback uses, so this
        // fails if a future refactor removes the TrySkipDispatch short-circuit
        // from RouteFfiEvent. Calling RouteFfiEvent from a fresh Thread simulates
        // Rust calling FFICallback from its own worker thread.
        [Test]
        public void RouteFfiEvent_InlineCallback_CompletesOnDispatchingThread()
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
                dispatchToMainThread: false);

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
                "Completion did not run — RouteFfiEvent may be missing the TrySkipDispatch call for inline entries.");
            Assert.AreEqual(dispatchThreadId, completionThreadId,
                "Inline completion did not run on the dispatching thread — RouteFfiEvent marshalled it instead.");
            Assert.AreNotEqual(testThreadId, completionThreadId,
                "Inline completion ran on the test main thread.");
        }

        // Integration test: entries registered with dispatchToMainThread:true must
        // reach the main-thread SynchronizationContext via Post. We swap _context
        // for a recording SC so we can observe the post (and drain it to verify
        // the queued lambda actually invokes the completion).
        [Test]
        public void RouteFfiEvent_MainThreadCallback_PostsToSynchronizationContext_AndDrainsCompletion()
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
                dispatchToMainThread: true);

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
                    "Main-thread completion ran synchronously on the dispatcher thread — it should have been posted, not executed.");
                Assert.AreEqual(1, recording.Posts.Count,
                    "RouteFfiEvent should have posted exactly one work item to the sync context for a main-thread pending entry.");

                // Drain the queued lambda on this (test) thread, simulating Unity's
                // main-thread queue drain. The completion must now run, and on this thread.
                var drainerThreadId = Thread.CurrentThread.ManagedThreadId;
                recording.DrainOnce();

                Assert.IsTrue(completionRan.Wait(TimeSpan.FromSeconds(1)),
                    "Drained queued post did not invoke the completion.");
                Assert.AreEqual(drainerThreadId, completionThreadId,
                    "Drained completion should run on the thread that drains the sync context.");
                Assert.AreNotEqual(dispatchThreadId, completionThreadId,
                    "Completion ran on the dispatcher's thread despite dispatchToMainThread:true.");
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
            // caller's thread regardless of dispatchToMainThread. What makes the
            // inline path "skip dispatch" is that FFICallback (the FFI thread) is
            // the caller, vs. the SynchronizationContext-posted lambda (the main
            // thread) for entries that require main-thread dispatch.
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
                dispatchToMainThread: true);

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
