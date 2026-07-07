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
    /// The remote-participant check polls <c>Room.RemoteParticipants</c> rather than the
    /// ParticipantConnected event so an agent delivered via the connect snapshot counts
    /// the same as one delivered via an event — the bug under investigation is the
    /// participant being absent from both.
    ///
    /// Gated by environment variables so CI (which runs no agent worker) skips it:
    ///   LK_TEST_AGENT_TOKEN_ENDPOINT — full token endpoint URL, or
    ///   LK_TEST_AGENT_SANDBOX_ID    — LiveKit Cloud sandbox id.
    /// </summary>
    public class AgentDispatchStressTests
    {
        const int Iterations = 100;
        const float AgentTimeoutSeconds = 10f;
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
                    try
                    {
                        var connect = room.Connect(fetch.Result.ServerUrl, fetch.Result.ParticipantToken, new RoomOptions());
                        yield return connect;
                        if (connect.IsError)
                            Assert.Fail($"Iteration {i}/{Iterations}: failed to connect");

                        var agentAppeared = new Expectation(
                            () => room.RemoteParticipants.Values.Any(p => p._info.Kind == Proto.ParticipantKind.Agent),
                            timeoutSeconds: AgentTimeoutSeconds);
                        yield return agentAppeared.Wait();

                        if (agentAppeared.Error != null)
                        {
                            var participants = string.Join(", ", room.RemoteParticipants.Keys);
                            Assert.Fail(
                                $"Iteration {i}/{Iterations}: no agent participant within {AgentTimeoutSeconds}s. " +
                                $"Room sid={room.Sid} name={room.Name} remoteParticipants=[{participants}]");
                        }

                        Debug.Log($"[AgentDispatchStress] Iteration {i}/{Iterations} OK (room sid={room.Sid} name={room.Name})");
                    }
                    finally
                    {
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

        static string? ReadEnv(string key)
        {
            var value = Environment.GetEnvironmentVariable(key)?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
