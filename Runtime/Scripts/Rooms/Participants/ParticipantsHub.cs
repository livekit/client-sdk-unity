using System.Collections.Generic;

namespace LiveKit.Rooms.Participants
{
    public class ParticipantsHub : IMutableParticipantsHub
    {
        private Participant? local;
        private readonly Dictionary<string, Participant> remoteParticipants = new();

        public Participant LocalParticipant()
        {
            return local
                   ?? throw new System.InvalidOperationException(
                       "Local participant not assigned yet"
                   );
        }

        public Participant? RemoteParticipant(string sid)
        {
            remoteParticipants.TryGetValue(sid, out var remoteParticipant);
            return remoteParticipant;
        }

        public void AssignLocal(Participant participant)
        {
            local = participant;
        }

        public void AddRemote(Participant participant)
        {
            remoteParticipants.Add(participant.Sid, participant);
        }

        public void RemoveRemote(Participant participant)
        {
            remoteParticipants.Remove(participant.Sid);
        }
    }
}