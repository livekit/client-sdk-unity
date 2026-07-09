using System.Collections;
using LiveKit.PlayModeTests.Utils;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// Densely samples the join-handshake race that agent dispatch hits in production:
    /// instead of a real agent, a second plain participant (the joiner) connects at a
    /// controlled millisecond offset around the observer's connect, sweeping the
    /// joiner's arrival across the observer's signal handshake — the window where a
    /// remote participant's announcement is suspected to go missing.
    ///
    /// Detection follows the same pattern as <see cref="AgentDispatchStressTests"/>:
    /// the observer's <c>Room.RemoteParticipants</c> is checked exactly once after the
    /// connects complete; afterwards only the ParticipantConnected event can fulfill
    /// the expectation. On timeout the failure message includes the sweep offset and
    /// the remote-participant count at that moment.
    ///
    /// Local-only by design: requires a dev server (livekit-server --dev) and no agent
    /// worker; enable by setting <see cref="Enabled"/> to true.
    /// </summary>
    public class ConcurrentJoinStressTests
    {
        static readonly bool Enabled = false;

        const int Iterations = 100;
        const float JoinTimeoutSeconds = 10f;
        const int TestTimeoutMs = 30 * 60 * 1000;
        // Sweep the joiner's connect offset relative to the observer's from
        // -250ms (joiner first) to +245ms (observer first) in 5ms steps.
        const int OffsetStartMs = -250;
        const int OffsetStepMs = 5;

        [UnityTest, Category("E2E"), Timeout(TestTimeoutMs)]
        public IEnumerator ConcurrentJoin_ObserverSeesJoiner_100Iterations()
        {
            if (!Enabled)
            {
                Assert.Ignore(
                    "Concurrent join test skipped: set Enabled = true to run it locally " +
                    "(requires a dev server, e.g. livekit-server --dev).");
                yield break;
            }

            LogAssert.ignoreFailingMessages = true;
            try
            {
                for (int i = 1; i <= Iterations; i++)
                {
                    var offsetMs = OffsetStartMs + (i - 1) * OffsetStepMs;

                    var observerOptions = TestRoomContext.ConnectionOptions.Default;
                    observerOptions.Identity = "concurrent-observer";
                    var joinerOptions = TestRoomContext.ConnectionOptions.Default;
                    joinerOptions.Identity = "concurrent-joiner";

                    using var context = new TestRoomContext(new[] { observerOptions, joinerOptions });
                    var observer = context.Rooms[0];
                    var joiner = context.Rooms[1];

                    var joinerAppeared = new Expectation(timeoutSeconds: JoinTimeoutSeconds);
                    Room.ParticipantDelegate onParticipantConnected = participant =>
                    {
                        if (participant.Identity == joinerOptions.Identity)
                            joinerAppeared.Fulfill();
                    };
                    // Subscribed before Connect: frames elapse while both connects are in
                    // flight, so a post-connect subscription could miss the event. The
                    // snapshot check below still runs exactly once after the connects.
                    observer.ParticipantConnected += onParticipantConnected;
                    try
                    {
                        ConnectInstruction observerConnect, joinerConnect;
                        if (offsetMs >= 0)
                        {
                            observerConnect = Connect(context, observer, observerOptions);
                            if (offsetMs > 0)
                                yield return new WaitForSecondsRealtime(offsetMs / 1000f);
                            joinerConnect = Connect(context, joiner, joinerOptions);
                        }
                        else
                        {
                            joinerConnect = Connect(context, joiner, joinerOptions);
                            yield return new WaitForSecondsRealtime(-offsetMs / 1000f);
                            observerConnect = Connect(context, observer, observerOptions);
                        }

                        while (!observerConnect.IsDone || !joinerConnect.IsDone)
                            yield return null;

                        if (observerConnect.IsError || joinerConnect.IsError)
                        {
                            Assert.Fail(
                                $"Iteration {i}/{Iterations} (offset {offsetMs}ms): connect failed " +
                                $"(observer={(observerConnect.IsError ? "error" : "ok")}, " +
                                $"joiner={(joinerConnect.IsError ? "error" : "ok")}) room={context.RoomName}");
                        }

                        string delivery;
                        if (observer.RemoteParticipants.ContainsKey(joinerOptions.Identity))
                        {
                            delivery = "snapshot";
                        }
                        else
                        {
                            yield return joinerAppeared.Wait();
                            if (joinerAppeared.Error != null)
                            {
                                var remotes = observer.RemoteParticipants;
                                Assert.Fail(
                                    $"Iteration {i}/{Iterations} (offset {offsetMs}ms): observer did not see " +
                                    $"joiner within {JoinTimeoutSeconds}s. Room sid={observer.Sid} name={observer.Name} " +
                                    $"remoteParticipantCount={remotes.Count} remoteParticipants=[{string.Join(", ", remotes.Keys)}]");
                            }
                            delivery = "event";
                        }

                        Debug.Log(
                            $"[ConcurrentJoinStress] Iteration {i}/{Iterations} OK " +
                            $"(offset={offsetMs}ms delivery={delivery} room={context.RoomName})");
                    }
                    finally
                    {
                        observer.ParticipantConnected -= onParticipantConnected;
                    }
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        static ConnectInstruction Connect(TestRoomContext context, Room room, TestRoomContext.ConnectionOptions options) =>
            room.Connect(TestRoomContext.ServerUrl, context.CreateToken(options), new RoomOptions { Dynacast = options.Dynacast });
    }
}
