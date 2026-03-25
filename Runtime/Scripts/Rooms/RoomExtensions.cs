using System;
using LiveKit.Rooms.Participants;
using Room = LiveKit.Rooms.Room;

namespace LiveKit.Rooms
{
    public static class RoomExtensions
    {
        public static LKParticipant ParticipantEnsured(this Room room, string identity)
        {
            return room.Participant(identity) ?? throw new Exception("Participant not found");
        }

        public static LKParticipant? Participant(this Room room, string identity)
        {
            if (identity == room.Participants.LocalParticipant().Identity)
                return room.Participants.LocalParticipant();

            return room.Participants.RemoteParticipant(identity);
        }
        
        public static LKParticipant RemoteParticipantEnsured(this IParticipantsHub hub, string sid)
        {
            return hub.RemoteParticipant(sid) ?? throw new Exception("Remote participant not found");
        }
    }
}
