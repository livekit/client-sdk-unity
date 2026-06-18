using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiveKit.Proto;
using LiveKit.PlayModeTests.Utils;
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

        // Deterministic coverage of the GetAwaiter surface added in Stage 1, using a
        // synthetic instruction so the awaiter logic is exercised without the FFI. These
        // are intentionally NOT [Category("E2E")] — they need no dev server. The real
        // connect-fail path stays covered by Connect_FailsWithInvalidUrl above; an earlier
        // E2E variant of these was flaky because the FFI emits its error log asynchronously,
        // which races LogAssert in the frame after the await has already resumed.
        private sealed class TestYieldInstruction : YieldInstruction
        {
            public void Complete() => IsDone = true;
            public void CompleteWithError() { IsError = true; IsDone = true; }
        }

        // OnCompleted path, success: await registers a continuation while the instruction is
        // still pending, then a non-error completion resumes it without throwing.
        [UnityTest]
        public IEnumerator GetAwaiter_ResumesOnCompletion_NoThrowOnSuccess()
        {
            var instruction = new TestYieldInstruction();
            var awaitTask = AwaitInstruction(instruction);
            Assert.IsFalse(awaitTask.IsCompleted, "Awaiter must not resume before IsDone");

            instruction.Complete();
            yield return new WaitUntil(() => awaitTask.IsCompleted);

            Assert.IsNull(awaitTask.Exception, awaitTask.Exception?.ToString());
            Assert.IsTrue(instruction.IsDone, "Awaiter resumed, so IsDone must be observable");
        }

        // OnCompleted path, failure: a completion with IsError makes the await throw — surfaced
        // here as a faulted task carrying a LiveKitException (the base instruction's default).
        [UnityTest]
        public IEnumerator GetAwaiter_ThrowsOnError()
        {
            var instruction = new TestYieldInstruction();
            var awaitTask = AwaitInstruction(instruction);
            Assert.IsFalse(awaitTask.IsCompleted, "Awaiter must not resume before IsDone");

            instruction.CompleteWithError();
            yield return new WaitUntil(() => awaitTask.IsCompleted);

            Assert.IsTrue(awaitTask.IsFaulted, "await must throw when the instruction completes with an error");
            Assert.IsInstanceOf<LiveKitException>(awaitTask.Exception?.InnerException);
        }

        // IsCompleted fast path: instruction is already done (no error) before it is awaited, so
        // the awaiter completes without ever registering a continuation and without throwing.
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

        private static async Task AwaitInstruction(YieldInstruction instruction)
        {
            await instruction;
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

        [UnityTest, Category("E2E"), Ignore("Known issue")]
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

        [UnityTest, Category("E2E"), Ignore("Known issue")]
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
    }
}