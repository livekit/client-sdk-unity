using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using LiveKit.PlayModeTests.Utils;
using Uniffi = uniffi.livekit_uniffi;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// A participant whose data streams run entirely through the UniFFI data stream managers.
    ///
    /// The UniFFI managers are transport-agnostic: the outgoing manager hands out encoded
    /// <c>livekit.DataPacket</c> bytes and the incoming manager consumes them. There is no UniFFI
    /// room yet, so this peer borrows a protobuf-FFI <see cref="Room"/> purely as that transport:
    /// every packet the outgoing manager produces is published as an opaque user data packet on
    /// <see cref="TunnelTopic"/>, and every user packet received on that topic is handed verbatim to
    /// the incoming manager. The FFI room never inspects user payloads, so the stream logic on both
    /// ends is exercised in UniFFI only.
    ///
    /// UniFFI tasks complete on the library's tokio threads. All FFI room calls are kept on the main
    /// thread by draining the outgoing queue from a coroutine and polling task completion.
    /// </summary>
    internal sealed class UniffiStreamPeer : IDisposable
    {
        public const string TunnelTopic = "uniffi-stream-tunnel";
        private const string PingTopic = "uniffi-stream-tunnel-ping";

        public Room Room { get; }
        public string Identity { get; }
        public Uniffi.OutgoingDataStreamManager Outgoing => _outgoing.Manager;
        public Uniffi.IncomingDataStreamManager Incoming => _incoming.Manager;

        /// <summary>Packets the outgoing manager produced and this peer published through the room.</summary>
        public int PacketsPublished { get; private set; }

        /// <summary>Tunnel packets received from the room and handed to the incoming manager.</summary>
        public int PacketsReceived { get; private set; }

        public int PingsReceived { get; private set; }

        /// <summary>Transport failures observed while pumping; empty in a healthy run.</summary>
        public List<string> Errors { get; } = new List<string>();

        private readonly Uniffi.PolledOutgoingDataStreamManager _outgoing;
        private readonly Uniffi.PolledIncomingDataStreamManager _incoming;
        private readonly Room.DataDelegate _onData;
        private readonly List<Task> _polls = new List<Task>();
        private GameObject? _host;
        private bool _stopping;

        /// <param name="registry">
        /// What the outgoing manager believes about remote participants. It decides the framing:
        /// see <see cref="FixedParticipantRegistry"/>.
        /// </param>
        /// <param name="maxIncomingPayloadBytes">
        /// Per-stream payload cap of the incoming manager; null for the library default.
        /// </param>
        public UniffiStreamPeer(
            Room room,
            string identity,
            Uniffi.RemoteParticipantRegistryDelegate registry,
            ulong? maxIncomingPayloadBytes = null)
        {
            Room = room;
            Identity = identity;
            _outgoing = Uniffi.LivekitUniffiMethods.PolledOutgoingDataStreamManager(registry);
            _incoming = Uniffi.LivekitUniffiMethods.PolledIncomingDataStreamManager(maxIncomingPayloadBytes);

            _onData = (data, participant, kind, topic) => OnDataReceived(data, topic);
            room.DataReceived += _onData;

            // The pump outlives any single test yield, so it needs a MonoBehaviour host.
            _host = new GameObject($"uniffi-stream-peer-{identity}");
            _host.AddComponent<CoroutineRunner>().StartCoroutine(PumpOutgoing());
        }

        /// <summary>
        /// Next stream opened by a remote participant. Readers of streams obtained through this method
        /// are disposed with the peer.
        /// </summary>
        public Task<Uniffi.OpenedStream?> NextOpenedStream() => Track(_incoming.Streams.NextOpenedStream());

        public Task<Uniffi.ClosedStream?> NextClosedStream() => Track(_incoming.Streams.NextClosedStream());

        /// <summary>
        /// Publishes pings until <paramref name="target"/> has received one or the timeout elapses.
        /// The SFU data path is not necessarily ready right after connect; check
        /// <see cref="PingsReceived"/> on the target afterwards.
        /// </summary>
        public IEnumerator WaitUntilReachable(UniffiStreamPeer target, float timeoutSeconds = 10f)
        {
            var start = Time.realtimeSinceStartup;
            var retryDelay = new WaitForSeconds(0.2f);
            while (target.PingsReceived == 0 && Time.realtimeSinceStartup - start < timeoutSeconds)
            {
                yield return Room.LocalParticipant.PublishData(new byte[] { 1 }, null, true, PingTopic);
                yield return retryDelay;
            }
        }

        private void OnDataReceived(byte[] data, string topic)
        {
            if (topic == PingTopic)
            {
                PingsReceived++;
                return;
            }
            if (topic != TunnelTopic) return;

            PacketsReceived++;
            // Cheap synchronous enqueue; decoding and reassembly run on the manager's Rust task.
            _incoming.Manager.HandlePacketReceived(new ReadOnlyMemory<byte>(data), Uniffi.EncryptionType.None);
        }

        // Drains the outgoing packet queue and publishes every packet through the FFI room, in order.
        private IEnumerator PumpOutgoing()
        {
            while (!_stopping)
            {
                var next = Track(_outgoing.Packets.NextPackets());
                while (!next.IsCompleted) yield return null;

                if (next.Status != TaskStatus.RanToCompletion)
                {
                    Errors.Add($"NextPackets {UniffiTasks.Describe(next)}");
                    yield break;
                }

                var batch = next.Result;
                if (batch == null) yield break; // queue closed

                foreach (var packet in batch)
                {
                    if (_stopping || Room.LocalParticipant == null) yield break;
                    var publish = Room.LocalParticipant.PublishData(packet.ToArray(), null, true, TunnelTopic);
                    yield return publish;
                    PacketsPublished++;
                    if (publish.IsError) Errors.Add($"PublishData failed: {publish.Error}");
                }
            }
        }

        private Task<T> Track<T>(Task<T> task)
        {
            lock (_polls) _polls.Add(task);
            return task;
        }

        public void Dispose()
        {
            if (_stopping) return;
            _stopping = true;

            Room.DataReceived -= _onData;
            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
            }

            // A pending poll holds a pointer to its queue, so wake every poll with null and let it
            // finish before the Rust objects are released.
            _outgoing.Packets.Close();
            _incoming.Streams.Close();
            Task[] pending;
            lock (_polls) pending = _polls.ToArray();
            try
            {
                Task.WaitAll(pending, TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Faulted polls are reported by the tests that issued them.
            }

            foreach (var poll in pending)
            {
                if (poll is Task<Uniffi.OpenedStream?> opened
                    && opened.Status == TaskStatus.RanToCompletion
                    && opened.Result != null)
                {
                    opened.Result.Dispose();
                }
            }

            _outgoing.Dispose();
            _incoming.Dispose();
        }
    }

    /// <summary>
    /// Fixed answers for the outgoing manager's remote-participant lookups, standing in for the room
    /// state a real host would consult. The answers decide the wire framing of every send.
    /// </summary>
    internal sealed class FixedParticipantRegistry : Uniffi.RemoteParticipantRegistryDelegate
    {
        private readonly string[] _identities;
        private readonly int _protocol;
        private readonly Uniffi.ClientCapability[] _capabilities;

        private FixedParticipantRegistry(string[] identities, int protocol, Uniffi.ClientCapability[] capabilities)
        {
            _identities = identities;
            _protocol = protocol;
            _capabilities = capabilities;
        }

        /// <summary>Recipients predating data streams v2: forces header, chunks, trailer framing.</summary>
        public static FixedParticipantRegistry Legacy(params string[] identities) =>
            new FixedParticipantRegistry(identities, 0, Array.Empty<Uniffi.ClientCapability>());

        /// <summary>
        /// Recipients on client protocol 2 advertising raw-deflate support: enables single-packet
        /// inline sends, compressed when that is smaller.
        /// </summary>
        public static FixedParticipantRegistry V2(params string[] identities) =>
            new FixedParticipantRegistry(identities, 2, new[] { Uniffi.ClientCapability.CompressionDeflateRaw });

        public int RemoteClientProtocol(string identity) => _protocol;
        public Uniffi.ClientCapability[] RemoteCapabilities(string identity) => _capabilities;
        public string[] RemoteIdentities() => _identities;
    }

    internal static class UniffiTasks
    {
        /// <summary>
        /// Yields until <paramref name="task"/> completes or the timeout elapses. Never throws;
        /// inspect the task afterwards.
        /// </summary>
        public static IEnumerator Await(Task task, float timeoutSeconds = 10f)
        {
            var start = Time.realtimeSinceStartup;
            while (!task.IsCompleted && Time.realtimeSinceStartup - start < timeoutSeconds)
                yield return null;
        }

        public static bool Succeeded(Task task) => task.Status == TaskStatus.RanToCompletion;

        public static string Describe(Task task) => task.Status switch
        {
            TaskStatus.RanToCompletion => "completed",
            TaskStatus.Faulted => $"faulted: {Flatten(task.Exception)}",
            TaskStatus.Canceled => "canceled",
            _ => $"did not complete in time ({task.Status})",
        };

        /// <summary>The exception a UniFFI task actually failed with, without the AggregateException wrapper.</summary>
        public static Exception? Flatten(Exception? exception) =>
            exception is AggregateException aggregate ? aggregate.Flatten().InnerException ?? aggregate : exception;
    }

    internal static class UniffiAvailability
    {
        public const string IgnoreReason =
            "liblivekit_uniffi is only built for macOS arm64 (Runtime/Plugins/ffi-macos-arm64)";

        public static bool IsAvailable =>
            (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
    }
}
