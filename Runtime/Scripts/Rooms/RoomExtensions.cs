using System;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms
{
    public static class RoomExtensions
    {
        public static Participant? Participant(this Room room, string sid)
        {
            if (sid == room.Participants.LocalParticipant().Sid)
                return room.Participants.LocalParticipant();

            return RemoteParticipant(room, sid);
        }
        
        public static Participant ParticipantEnsured(this Room room, string sid)
        {
            return room.Participant(sid) ?? throw new Exception("Participant not found");
        }
        
        public static Participant? RemoteParticipant(this Room room, string sid)
        {
            return room.Participants.RemoteParticipant(sid);
        }
        
        public static Participant RemoteParticipantEnsured(this Room room, string sid)
        {
            return room.RemoteParticipant(sid) ?? throw new Exception("Remote participant not found");
        }
    }
}