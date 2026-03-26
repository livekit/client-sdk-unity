using System;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms
{
    public static class RoomExtensions
    {
        public static Participant ParticipantEnsured(this Room room, string identity)
        {
            return room.Participant(identity) ?? throw new Exception("Participant not found");
        }

        public static Participant? Participant(this Room room, string identity)
        {
            if (identity == room.Participants.LocalParticipant().Identity)
                return room.Participants.LocalParticipant();

            return room.Participants.RemoteParticipant(identity);
        }
        
        public static Participant RemoteParticipantEnsured(this IParticipantsHub hub, string sid)
        {
            return hub.RemoteParticipant(sid) ?? throw new Exception("Remote participant not found");
        }
    }
}