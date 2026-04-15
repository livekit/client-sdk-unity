using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;

namespace LiveKit.PlayModeTests
{
    public class FfiHandleDisposalTests
    {
        /// <summary>
        /// Reproduces a Rust panic that occurs when a stream writer's FfiHandle is
        /// released by the GC finalizer thread instead of being explicitly disposed.
        /// The Rust drop implementation for outgoing data streams requires a Tokio
        /// runtime, which doesn't exist on the GC finalizer thread.
        ///
        /// Before the fix: Unity crashes with SIGABRT (Abort trap: 6).
        /// After the fix:  The handle drop is marshaled to the main thread and
        ///                 Unity continues running normally.
        /// </summary>
        // Known issue: crashes Unity on Linux CI (batchmode) with exit code 134.
        // The FfiHandle.ReleaseHandle fix marshals the Rust drop to the main thread via
        // SynchronizationContext, but in batchmode on Linux the context may not be
        // available, so the GC finalizer still calls FfiDropHandle on a non-Tokio thread.
        [UnityTest, Category("E2E")]
        public IEnumerator StreamWriter_LeakedHandle_DoesNotCrashOnGC()
        {
            LogAssert.ignoreFailingMessages = true;

            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "gc-sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "gc-receiver";

            using var context = new TestRoomContext(new[] { sender, receiver });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            // Open a text stream writer in a helper method so all references
            // to the writer and its instruction go out of scope, making the
            // writer eligible for GC finalization.
            yield return OpenAndLeakWriter(context);

            // Force the GC to collect the orphaned writer and run its finalizer.
            // Before the fix, FfiHandle.ReleaseHandle() runs on the GC finalizer
            // thread, calling NativeMethods.FfiDropHandle() into Rust, which
            // panics because there is no Tokio runtime on that thread.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            // Yield frames to let any marshaled main-thread work execute
            // (after the fix, the drop is posted via SynchronizationContext)
            yield return null;
            yield return null;

            // If we reach this point, Unity didn't crash — the fix works.
            Assert.Pass("FfiHandle was safely released without crashing Unity");
        }

        /// <summary>
        /// Opens a stream writer and returns without disposing it.
        /// Once this method returns, the writer and instruction are only
        /// reachable through the GC — no live references remain on the stack.
        /// </summary>
        private IEnumerator OpenAndLeakWriter(TestRoomContext context)
        {
            var streamInstruction = context.Rooms[0].LocalParticipant.StreamText("gc-test-topic");
            yield return streamInstruction;
            Assert.IsFalse(streamInstruction.IsError, "StreamText open failed");
            Assert.IsNotNull(streamInstruction.Writer, "Writer should not be null");
            // Method returns — streamInstruction and its Writer become unreachable
        }
    }
}
