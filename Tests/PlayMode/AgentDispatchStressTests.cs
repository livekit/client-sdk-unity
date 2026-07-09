using System;
using System.Collections;
using System.Linq;
using LiveKit.Internal.Threading;
using LiveKit.PlayModeTests.Utils;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// Stress reproduction for field reports of dispatched agents missing from both the
    /// connect snapshot and the room events delivered after the ReadyForRoomEvent
    /// handshake. Each iteration fetches fresh connection details from a token source
    /// whose server dispatches an agent into the room, connects, and requires a remote
    /// participant of kind Agent to become visible within <see cref="AgentTimeoutSeconds"/>.
    /// The run stops at the first failure and logs the room sid/name so the failing room
    /// can be cross-checked against server-side dispatch logs.
    ///
    /// Detection distinguishes the two delivery mechanisms: <c>Room.RemoteParticipants</c>
    /// is checked exactly once after Connect returns (the snapshot path); afterwards only
    /// the ParticipantConnected event can fulfill the expectation. On timeout the failure
    /// message includes the remote-participant count at that moment — a count above zero
    /// means the agent reached client state but its event never fired, zero means it never
    /// reached the client at all.
    ///
    /// Gated by environment variables so CI (which runs no agent worker) skips it:
    ///   LK_TEST_AGENT_TOKEN_ENDPOINT — full token endpoint URL, or
    ///   LK_TEST_AGENT_SANDBOX_ID    — LiveKit Cloud sandbox id.
    /// </summary>
    public class AgentDispatchStressTests
    {
        const int Iterations = 100;
        const float AgentTimeoutSeconds = 10f;
        // The token service rate-limits room creation, so idle between iterations.
        const float IterationCooldownSeconds = 5f;
        const int TestTimeoutMs = 30 * 60 * 1000;

        const string SandBoxId = null;
        const string Endpoint = null;
        const string AgentName = null;

        [UnityTest, Category("AgentE2E"), Timeout(TestTimeoutMs)]
        public IEnumerator AgentDispatch_AgentAppearsWithinTimeout_100Iterations()
        {
            var tokenSource = CreateTokenSource();
            if (tokenSource == null)
            {
                Assert.Ignore(
                    "Agent dispatch test skipped: set SandBoxId or " +
                    "Endpoint to run it.");
                yield break;
            }

            LogAssert.ignoreFailingMessages = true;
            try
            {
                for (int i = 1; i <= Iterations; i++)
                {
                    if (i > 1)
                        yield return new WaitForSecondsRealtime(IterationCooldownSeconds);

                    var fetch = new TaskYieldInstruction<ConnectionDetails>(
                        tokenSource.FetchConnectionDetails(new TokenSourceFetchOptions
                        {
                            RoomName = $"unity-agent-test-{Guid.NewGuid()}",
                            ParticipantIdentity = $"unity-agent-test-participant-{i}",
                            AgentName = AgentName
                            
                        }));
                    yield return fetch;
                    if (fetch.IsError)
                        Assert.Fail($"Iteration {i}/{Iterations}: token fetch failed: {fetch.Exception}");
                    if (string.IsNullOrEmpty(fetch.Result?.ServerUrl) || string.IsNullOrEmpty(fetch.Result?.ParticipantToken))
                        Assert.Fail($"Iteration {i}/{Iterations}: token source returned incomplete connection details");

                    var room = new Room();
                    Room.ParticipantDelegate? onParticipantConnected = null;
                    try
                    {
                        var connect = room.Connect(fetch.Result.ServerUrl, fetch.Result.ParticipantToken, new RoomOptions());
                        yield return connect;
                        if (connect.IsError)
                            Assert.Fail($"Iteration {i}/{Iterations}: failed to connect");

                        var agentAppeared = new Expectation(timeoutSeconds: AgentTimeoutSeconds);
                        onParticipantConnected = participant =>
                        {
                            if (participant._info.Kind == Proto.ParticipantKind.Agent)
                                agentAppeared.Fulfill();
                        };
                        // Subscribe before the one-shot snapshot check; with no yield in
                        // between, an agent can't slip through the gap. After this check
                        // only the ParticipantConnected event can fulfill the expectation.
                        room.ParticipantConnected += onParticipantConnected;

                        string delivery;
                        if (room.RemoteParticipants.Values.Any(p => p._info.Kind == Proto.ParticipantKind.Agent))
                        {
                            delivery = "snapshot";
                        }
                        else
                        {
                            yield return agentAppeared.Wait();
                            if (agentAppeared.Error != null)
                            {
                                var remotes = room.RemoteParticipants;
                                Assert.Fail(
                                    $"Iteration {i}/{Iterations}: no ParticipantConnected for an agent within {AgentTimeoutSeconds}s. " +
                                    $"Room sid={room.Sid} name={room.Name} remoteParticipantCount={remotes.Count} " +
                                    $"remoteParticipants=[{string.Join(", ", remotes.Keys)}]");
                            }
                            delivery = "event";
                        }

                        Debug.Log($"[AgentDispatchStress] Iteration {i}/{Iterations} OK (room sid={room.Sid} name={room.Name} delivery={delivery})");
                    }
                    finally
                    {
                        if (onParticipantConnected != null)
                            room.ParticipantConnected -= onParticipantConnected;
                        room.Disconnect();
                    }
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        static ITokenSourceConfigurable? CreateTokenSource()
        {
            if (Endpoint != null)
                return new TokenSourceEndpoint(Endpoint, null);

            if (SandBoxId != null)
                return new TokenSourceSandbox(SandBoxId);

            return null;
        }
    }
}
