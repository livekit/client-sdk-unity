using System.Collections;
using NUnit.Framework;
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

            using var context = new TestRoomContext(options);
            yield return context.ConnectAll();
            if (context.ConnectionError == null)
                Assert.Fail("Expected connection to fail");
        }

        [UnityTest, Category("E2E"), Ignore("Known issue")]
        public IEnumerator RoomName_MatchesProvided()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

            Assert.AreEqual(context.RoomName, context.Rooms[0].Name);
        }

        [UnityTest, Category("E2E"), Ignore("Known issue")]
        public IEnumerator RoomSid_StartsWithRM()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

            StringAssert.StartsWith("RM_", context.Rooms[0].Sid);
        }

        [UnityTest, Category("E2E"), Ignore("Known issueI")]
        public IEnumerator ConnectionState_IsConnected()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

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
            yield return context.ConnectAll();
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

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

            yield return expectation.Wait();
            if (expectation.Error != null) Assert.Fail(expectation.Error);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator ParticipantJoin_TriggersEvent()
        {
            var first = TestRoomContext.ConnectionOptions.Default;
            var second = TestRoomContext.ConnectionOptions.Default;
            second.Identity = "second-participant";

            using var context = new TestRoomContext(new[] { first, second });
            yield return context.ConnectAll();
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

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
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

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

        [UnityTest, Category("E2E"), Ignore("Known issue: CLT-1415")]
        public IEnumerator Disconnect_TriggersEvent()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            if (context.ConnectionError != null)
                Assert.Fail(context.ConnectionError);

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
            room.Disconnect();

            yield return expectation.Wait();
            if (expectation.Error != null) Assert.Fail(expectation.Error);
        }
    }
}