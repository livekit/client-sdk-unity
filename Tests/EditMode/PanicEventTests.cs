using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using LiveKit.Internal.FFI;
namespace LiveKit.EditModeTests
{
    public class PanicEventTests
    {
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

        // Seeded in a different high-bit range than SkipDispatchTests so the two
        // fixtures cannot collide in FfiClient.Instance's shared pendingCallbacks map.
        private static long _asyncIdSeed = 0x7FE0_0000_0000_0000L;
        private static ulong NextAsyncId() => (ulong)Interlocked.Increment(ref _asyncIdSeed);

        [Test]
        public void RouteFfiEvent_Panic_RaisesPanicReceivedOnMainThread_AndCancelsPendingCallbacks()
        {
            var asyncId = NextAsyncId();
            var completed = false;
            var canceled = false;

            // Stands in for any in-flight async request (e.g. a pending
            // ConnectInstruction). After a panic its Rust-side task may be dead,
            // so it must be cancelled rather than left to hang forever.
            FfiClient.Instance.RegisterPendingCallback<UnpublishTrackCallback>(
                asyncId,
                static e => e.UnpublishTrack,
                cb => { completed = true; },
                onCancel: () => { canceled = true; });

            string receivedMessage = null;
            PanicReceivedDelegate handler = e => receivedMessage = e.Message;
            FfiClient.Instance.PanicReceived += handler;

            var recording = new RecordingSyncContext();
            var originalContext = FfiClient.Instance._context;
            FfiClient.Instance._context = recording;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("FFI panic"));

                var ev = new FfiEvent { Panic = new Panic { Message = "test panic from FFI" } };
                var dispatcher = new Thread(() => FfiClient.RouteFfiEvent(ev));
                dispatcher.Start();
                Assert.IsTrue(dispatcher.Join(TimeSpan.FromSeconds(2)),
                    "Dispatcher thread did not finish within 2s.");

                // Panic handling fires user-facing events (room teardown), so it must
                // be marshalled to the main thread, not run on the FFI callback thread.
                Assert.AreEqual(1, recording.Posts.Count,
                    "RouteFfiEvent should post the panic to the main-thread sync context.");
                Assert.IsNull(receivedMessage,
                    "PanicReceived ran on the FFI callback thread instead of the main-thread drain.");
                Assert.IsFalse(canceled,
                    "Pending callbacks were cancelled before the main-thread drain.");

                recording.DrainOnce();

                Assert.AreEqual("test panic from FFI", receivedMessage,
                    "PanicReceived did not fire with the panic message.");
                Assert.IsTrue(canceled,
                    "Pending callbacks were not cancelled on panic — awaiting instructions would hang forever.");
                Assert.IsFalse(completed,
                    "The pending callback completed instead of being cancelled.");

                // The pending entry must be gone: a late callback for it is a no-op.
                var lateCallback = new FfiEvent
                {
                    UnpublishTrack = new UnpublishTrackCallback { AsyncId = asyncId }
                };
                Assert.IsFalse(FfiClient.Instance.TryDispatchPendingCallback(asyncId, lateCallback),
                    "The cancelled pending entry is still registered.");
            }
            finally
            {
                FfiClient.Instance._context = originalContext;
                FfiClient.Instance.PanicReceived -= handler;
                FfiClient.Instance.CancelPendingCallback(asyncId);
            }
        }
    }
}
