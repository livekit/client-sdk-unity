using System.Collections;
using System.Collections.Generic;
using LiveKit.PlayModeTests.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// Reproduces the "agent dispatched at connect time" report: a remote
    /// participant that is already in the room when the local participant
    /// connects arrives in the connect snapshot instead of as a
    /// ParticipantConnected delta — and the SDK never raises
    /// ParticipantConnected for it, even for handlers wired before Connect().
    /// Apps that drive their "remote participant joined → subscribe/render"
    /// logic purely from that event never learn the participant exists,
    /// although it is present in Room.RemoteParticipants.
    ///
    /// Whether a concurrently connecting participant (typically an agent)
    /// lands in the snapshot or in the event stream is a server-side race, so
    /// in the field this manifests intermittently. The test makes the losing
    /// side deterministic by fully connecting the "agent" first.
    /// </summary>
    public class SnapshotParticipantEventsTests
    {
        const float ControlEventTimeoutSeconds = 15f;

        static (TestRoomContext.ConnectionOptions agent,
                TestRoomContext.ConnectionOptions subscriber,
                TestRoomContext.ConnectionOptions lateJoiner) ThreePeers()
        {
            var agent = TestRoomContext.ConnectionOptions.Default;
            agent.Identity = "snapshot-agent";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "snapshot-subscriber";
            var lateJoiner = TestRoomContext.ConnectionOptions.Default;
            lateJoiner.Identity = "snapshot-late-joiner";
            return (agent, subscriber, lateJoiner);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Connect_RaisesParticipantConnected_ForParticipantAlreadyInRoom()
        {
            var (agentOptions, subscriberOptions, lateJoinerOptions) = ThreePeers();
            using var context = new TestRoomContext(new[] { agentOptions, subscriberOptions, lateJoinerOptions });

            // 1. The "agent" is fully connected before the subscriber starts
            //    connecting, guaranteeing it is part of the subscriber's
            //    connect snapshot rather than a ParticipantConnected delta.
            yield return context.ConnectRoom(0);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var agentIdentity = context.Rooms[0].LocalParticipant.Identity;
            var lateJoinerIdentity = lateJoinerOptions.Identity;
            var subscriberRoom = context.Rooms[1];

            // 2. Wire ParticipantConnected BEFORE Connect() — the earliest
            //    possible subscription an app can make.
            var connectedIdentities = new List<string>();
            subscriberRoom.ParticipantConnected += participant =>
            {
                lock (connectedIdentities) connectedIdentities.Add(participant.Identity);
            };

            // 3. Subscriber joins; the agent is already in the room.
            yield return context.ConnectRoom(1);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            // 4. Control: a participant joining AFTER the subscriber must fire
            //    ParticipantConnected via the regular delta path. Room events
            //    are delivered in order, so once the control event has fired,
            //    any event for the agent would already have been dispatched.
            //    This keeps the failing case fast and proves the handler
            //    wiring works.
            yield return context.ConnectRoom(2);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var controlEvent = new Expectation(
                predicate: () =>
                {
                    lock (connectedIdentities) return connectedIdentities.Contains(lateJoinerIdentity);
                },
                timeoutSeconds: ControlEventTimeoutSeconds);
            yield return controlEvent.Wait();
            Assert.IsNull(controlEvent.Error,
                $"Control failed: ParticipantConnected never fired for the late joiner " +
                $"'{lateJoinerIdentity}' — event delivery is broken beyond the snapshot case. " +
                $"Received: [{string.Join(", ", connectedIdentities)}]");

            // 5. The snapshot data itself must have arrived: the agent is
            //    visible in RemoteParticipants. This isolates the defect to
            //    event emission, not data delivery.
            Assert.IsTrue(subscriberRoom.RemoteParticipants.ContainsKey(agentIdentity),
                $"Agent '{agentIdentity}' missing from RemoteParticipants — snapshot itself was lost");

            // 6. The repro assertion: ParticipantConnected must also fire for
            //    the participant that was already in the room at connect time.
            bool agentConnectedFired;
            lock (connectedIdentities) agentConnectedFired = connectedIdentities.Contains(agentIdentity);
            Assert.IsTrue(agentConnectedFired,
                $"ParticipantConnected never fired for '{agentIdentity}', which was already in the " +
                $"room when the subscriber connected (snapshot participant). It IS present in " +
                $"RemoteParticipants, so only the event is missing. " +
                $"Received: [{string.Join(", ", connectedIdentities)}]");
        }
    }
}
