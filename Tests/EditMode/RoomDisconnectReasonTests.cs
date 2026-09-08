using System;
using LiveKit.Internal;
using LiveKit.Proto;
using NUnit.Framework;

using LiveKit.Internal.FFI;
namespace LiveKit.EditModeTests
{
    // Drives Room.OnEventReceived with synthetic FFI events to verify that the
    // DisconnectReason carried by the native layer is surfaced through the public
    // API. A zero FfiHandle is treated as invalid by the SafeHandle, so disposal
    // is a no-op and no native FFI drop is attempted; matching the event's
    // RoomHandle to it (also 0) lets OnEventReceived process the event.
    public class RoomDisconnectReasonTests
    {
        [Test]
        public void DisconnectReason_DefaultsToUnknown()
        {
            var room = new Room();
            Assert.AreEqual(DisconnectReason.UnknownReason, room.DisconnectReason);
        }

        [Test]
        public void Disconnected_SurfacesReasonOnPropertyAndEvent()
        {
            var room = new Room();
            room.RoomHandle = new FfiHandle(IntPtr.Zero);

            DisconnectReason? eventReason = null;
            Room eventRoom = null;
            room.DisconnectedWithReason += (r, reason) =>
            {
                eventRoom = r;
                eventReason = reason;
            };

            room.OnEventReceived(new RoomEvent
            {
                RoomHandle = 0,
                Disconnected = new Disconnected { Reason = DisconnectReason.ServerShutdown }
            });

            Assert.AreEqual(DisconnectReason.ServerShutdown, room.DisconnectReason,
                "Room.DisconnectReason should reflect the reason from the FFI event.");
            Assert.AreEqual(DisconnectReason.ServerShutdown, eventReason,
                "DisconnectedWithReason should fire carrying the reason.");
            Assert.AreSame(room, eventRoom);
        }

        [Test]
        public void ParticipantDisconnected_SurfacesReasonOnEvent()
        {
            var room = new Room();
            room.RoomHandle = new FfiHandle(IntPtr.Zero);

            const string identity = "remote-participant";
            // Id 0 -> invalid FfiHandle, so the participant carries no live native handle.
            room.CreateRemoteParticipant(new OwnedParticipant
            {
                Handle = new FfiOwnedHandle { Id = 0 },
                Info = new ParticipantInfo { Identity = identity }
            });

            DisconnectReason? eventReason = null;
            Participant eventParticipant = null;
            room.ParticipantDisconnectedWithReason += (participant, reason) =>
            {
                eventParticipant = participant;
                eventReason = reason;
            };

            room.OnEventReceived(new RoomEvent
            {
                RoomHandle = 0,
                ParticipantDisconnected = new ParticipantDisconnected
                {
                    ParticipantIdentity = identity,
                    DisconnectReason = DisconnectReason.ParticipantRemoved
                }
            });

            Assert.AreEqual(DisconnectReason.ParticipantRemoved, eventReason,
                "ParticipantDisconnectedWithReason should fire carrying the reason.");
            Assert.IsNotNull(eventParticipant);
            Assert.AreEqual(identity, eventParticipant.Identity);
        }

        [Test]
        public void Disconnected_ReentrantDisconnectFromHandlers_KeepsServerReason()
        {
            var room = new Room();
            room.RoomHandle = new FfiHandle(IntPtr.Zero);
            // A fresh Room already reads ConnDisconnected (the enum default), so record a
            // connected state first, as the core's own event would after a connect.
            room.OnEventReceived(new RoomEvent
            {
                RoomHandle = 0,
                ConnectionStateChanged = new ConnectionStateChanged { State = ConnectionState.ConnConnected }
            });
            Assert.AreEqual(ConnectionState.ConnConnected, room.ConnectionState);

            var reports = 0;
            DisconnectReason? reasonSeenByStateHandler = null;
            room.ConnectionStateChanged += state =>
            {
                if (state != ConnectionState.ConnDisconnected) return;
                reasonSeenByStateHandler = room.DisconnectReason;
                // An app that disconnects "to be sure" from its state handler.
                room.Disconnect();
            };
            room.DisconnectedWithReason += (_, __) =>
            {
                reports++;
                room.Disconnect();
            };

            // The core reports a server-side disconnect as two queued events.
            room.OnEventReceived(new RoomEvent
            {
                RoomHandle = 0,
                ConnectionStateChanged = new ConnectionStateChanged { State = ConnectionState.ConnDisconnected }
            });
            room.OnEventReceived(new RoomEvent
            {
                RoomHandle = 0,
                Disconnected = new Disconnected { Reason = DisconnectReason.ServerShutdown }
            });

            Assert.AreEqual(1, reports, "the disconnect is reported once");
            Assert.AreEqual(DisconnectReason.ServerShutdown, room.DisconnectReason,
                "a re-entrant Disconnect() must not replace the server's reason with ClientInitiated");
            Assert.AreEqual(DisconnectReason.ServerShutdown, reasonSeenByStateHandler,
                "the ConnectionStateChanged handler sees the reason already recorded");
            Assert.AreEqual(ConnectionState.ConnDisconnected, room.ConnectionState);
        }
    }
}
