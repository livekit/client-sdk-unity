using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiveKit.Proto;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Internal.Threading;
using YieldInstruction = LiveKit.Internal.Threading.YieldInstruction;
namespace LiveKit.PlayModeTests
{
    public class RoomTests
    {
        [UnityTest, Category("E2E")]
        public IEnumerator Connect_FailsWithInvalidUrl()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.ServerUrl = "invalid-url";

            // Suppress error logs from the native layer so they don't fail the test.
            // We use ignoreFailingMessages instead of LogAssert.Expect because the native
            // error log only appears when LK_VERBOSE is defined.
            LogAssert.ignoreFailingMessages = true;

            using var context = new TestRoomContext(options);
            yield return context.ConnectAll();

            LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(context.ConnectionError, "Expected connection to fail");
        }

        // Deterministic coverage of the awaiter using a synthetic instruction, so the logic is
        // exercised without the FFI (no dev server needed — hence not [Category("E2E")]). A live
        // connect would be non-deterministic here: the FFI emits its error log asynchronously and
        // would race LogAssert. The connect-fail path itself is covered by Connect_FailsWithInvalidUrl.
        private sealed class TestYieldInstruction : YieldInstruction
        {
            public void Complete() => IsDone = true;
            public void CompleteWithError() { IsError = true; IsDone = true; }
        }

        // OnCompleted path: await registers a continuation while the instruction is still
        // pending, then completion fires it and IsError is visible on resume.
        [UnityTest]
        public IEnumerator GetAwaiter_ResumesOnCompletion_AndSurfacesIsError()
        {
            var instruction = new TestYieldInstruction();
            var awaitTask = AwaitInstruction(instruction);
            Assert.IsFalse(awaitTask.IsCompleted, "Awaiter must not resume before IsDone");

            instruction.CompleteWithError();
            yield return new WaitUntil(() => awaitTask.IsCompleted);

            Assert.IsNull(awaitTask.Exception, awaitTask.Exception?.ToString());
            Assert.IsTrue(instruction.IsDone, "Awaiter resumed, so IsDone must be observable");
            Assert.IsTrue(instruction.IsError, "IsError must be visible on resume");
        }

        // IsCompleted fast path: instruction is already done before it is awaited, so the
        // awaiter completes without ever registering a continuation.
        [UnityTest]
        public IEnumerator GetAwaiter_CompletesImmediately_WhenAlreadyDone()
        {
            var instruction = new TestYieldInstruction();
            instruction.Complete();

            var awaitTask = AwaitInstruction(instruction);
            yield return new WaitUntil(() => awaitTask.IsCompleted);

            Assert.IsNull(awaitTask.Exception, awaitTask.Exception?.ToString());
            Assert.IsTrue(instruction.IsDone);
            Assert.IsFalse(instruction.IsError);
        }

        // An await continuation must resume on the Unity main thread even when the instruction
        // completes on a background thread — which is what the FFI callback thread does for
        // operations registered dispatchToMainThread:false (SetMetadata, stream writes, …).
        // Coroutines always resume on the main thread; the await path must match so callers can
        // safely touch Unity APIs after the await. RED until the awaiter marshals continuations
        // to the main SynchronizationContext.
        [UnityTest]
        public IEnumerator GetAwaiter_ResumesOnMainThread_WhenCompletedOffThread()
        {
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var instruction = new TestYieldInstruction();

            var awaitTask = AwaitAndGetResumeThread(instruction);
            Assert.IsFalse(awaitTask.IsCompleted, "Awaiter must not resume before IsDone");

            // Complete from a thread-pool thread, mimicking the FFI callback thread.
            Task.Run(() => instruction.Complete());

            yield return new WaitUntil(() => awaitTask.IsCompleted);

            Assert.AreEqual(mainThreadId, awaitTask.Result,
                "await must resume on the Unity main thread, not the thread that completed the instruction");
        }

        private static async Task AwaitInstruction(YieldInstruction instruction)
        {
            await instruction;
        }

        private static async Task<int> AwaitAndGetResumeThread(YieldInstruction instruction)
        {
            await instruction;
            return Thread.CurrentThread.ManagedThreadId;
        }

        [UnityTest, Category("E2E")]
        public IEnumerator RoomName_MatchesProvided()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            Assert.AreEqual(context.RoomName, context.Rooms[0].Name);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator RoomSid_StartsWithRM()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            StringAssert.StartsWith("RM_", context.Rooms[0].Sid);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator ConnectionState_IsConnected()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var room = context.Rooms[0];
            Assert.IsTrue(room.IsConnected);
            Assert.AreEqual(ConnectionState.ConnConnected, room.ConnectionState);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator ParticipantJoin_CreatesRemoteParticipant()
        {
            var first = TestRoomContext.ConnectionOptions.Default;
            var second = TestRoomContext.ConnectionOptions.Default;
            second.Identity = "second-participant";

            using var context = new TestRoomContext(new[] { first, second });

            var room = context.Rooms[0];
            var expectation = new Expectation(timeoutSeconds: 10f);
            room.ParticipantConnected += (participant) =>
            {
                if (!room.RemoteParticipants.TryGetValue(second.Identity, out var remoteParticipant))
                {
                    expectation.Fail($"Remote participant not created");
                    return;
                }
                if (remoteParticipant.Identity != second.Identity)
                {
                    expectation.Fail($"Unexpected participant identity: {remoteParticipant.Identity}");
                } else {
                    expectation.Fulfill();
                }
            };

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            yield return expectation.Wait();
            if (expectation.Error != null) Assert.Fail(expectation.Error);
        }

        [UnityTest, Category("E2E"), Ignore("Known issue")]
        public IEnumerator ParticipantJoin_TriggersEvent()
        {
            var first = TestRoomContext.ConnectionOptions.Default;
            var second = TestRoomContext.ConnectionOptions.Default;
            second.Identity = "second-participant";

            using var context = new TestRoomContext(new[] { first, second });

            var room = context.Rooms[0];
            var expectation = new Expectation(timeoutSeconds: 10f);
            room.ParticipantConnected += (participant) =>
            {
                if (participant.Identity == second.Identity)
                {
                    expectation.Fulfill();
                    return;
                }
                expectation.Fail($"Unexpected participant identity: {participant}");
            };

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            yield return expectation.Wait();
            if (expectation.Error != null) Assert.Fail(expectation.Error);
        }

        [UnityTest, Category("E2E"), Ignore("Known issue")]
        public IEnumerator ParticipantDisconnect_TriggersEvent()
        {
            var first = TestRoomContext.ConnectionOptions.Default;
            var second = TestRoomContext.ConnectionOptions.Default;
            second.Identity = "second-participant";

            using var context = new TestRoomContext(new[] { first, second });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var room = context.Rooms[0];
            var expectation = new Expectation(timeoutSeconds: 10f);
            room.ParticipantDisconnected += (participant) =>
            {
                if (participant.Identity == second.Identity)
                {
                    expectation.Fulfill();
                    return;
                }
                expectation.Fail($"Unexpected participant identity: {participant}");
            };

            // Disconnect the second participant
            context.Rooms[1].Disconnect();

            yield return expectation.Wait();
            if (expectation.Error != null) Assert.Fail(expectation.Error);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Disconnect_TriggersEvent()
        {
            using var context = new TestRoomContext();

            var room = context.Rooms[0];
            var expectation = new Expectation();
            var invocation = 0;
            room.ConnectionStateChanged += (state) =>
            {
                if (invocation == 0)
                {
                    if (state != ConnectionState.ConnConnected)
                        expectation.Fail($"Expected connected, but got {state}");
                }
                else if (invocation == 1)
                {
                    if (state != ConnectionState.ConnDisconnected)
                        expectation.Fail($"Expected disconnected, but got {state}");
                    else
                        expectation.Fulfill();
                }
                else
                {
                    expectation.Fail($"Extraneous state change: {state}");
                }
                invocation++;
            };
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);
            room.Disconnect();

            yield return expectation.Wait();
            if (expectation.Error != null) Assert.Fail(expectation.Error);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Connect_ReportsConnectedOnce()
        {
            using var context = new TestRoomContext();
            var room = context.Rooms[0];
            var connectedReports = 0;
            room.ConnectionStateChanged += s =>
            {
                if (s == ConnectionState.ConnConnected) connectedReports++;
            };

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);
            Assert.AreEqual(1, connectedReports, "recorded from the connect callback");

            // The core's own ConnectionStateChanged(Connected) is queued behind the connect
            // callback and drains on a later frame; it must be absorbed as a repeat.
            for (var i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(1, connectedReports, "the core's copy of the transition must not be reported again");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Disconnect_ReportsClientInitiated_OnceWithRoomIntact()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);
            var room = context.Rooms[0];

            var disconnected = 0;
            var disconnectedWithReason = 0;
            DisconnectReason? reason = null;
            room.Disconnected += r =>
            {
                disconnected++;
                // Handlers run before the release, with the state already recorded.
                Assert.IsNotNull(r.RoomHandle, "the room handle must still be live in the handler");
                Assert.IsFalse(r.LocalParticipant.Handle.IsClosed, "participant handles must still be live in the handler");
                Assert.AreEqual(ConnectionState.ConnDisconnected, r.ConnectionState);
                Assert.AreEqual(DisconnectReason.ClientInitiated, r.DisconnectReason);
                // A teardown handler that hangs up "to be sure" must not re-report.
                r.Disconnect();
            };
            room.DisconnectedWithReason += (_, r) =>
            {
                disconnectedWithReason++;
                reason = r;
            };

            // Dispose is a disconnect too. Reported synchronously: no frame has to pass
            // for a hang-up to be observable.
            room.Dispose();

            Assert.AreEqual(1, disconnected);
            Assert.AreEqual(1, disconnectedWithReason);
            Assert.AreEqual(DisconnectReason.ClientInitiated, reason);
            Assert.AreEqual(ConnectionState.ConnDisconnected, room.ConnectionState);
            Assert.IsNull(room.RoomHandle, "the room is released once the handlers ran");

            // A later Disconnect (TestRoomContext.Dispose issues one too) reports nothing.
            room.Disconnect();
            Assert.AreEqual(1, disconnected);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Disconnect_ThrowingHandler_StillReleasesRoom()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);
            var room = context.Rooms[0];

            room.Disconnected += _ => throw new System.InvalidOperationException("handler failed");

            Assert.Throws<System.InvalidOperationException>(() => room.Disconnect());

            // The handler's exception surfaces, but the room is still released.
            Assert.IsNull(room.RoomHandle, "the release must run even when a Disconnected handler throws");
            Assert.DoesNotThrow(() => room.Dispose());
        }
    }
}