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
    }
}
