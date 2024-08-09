using System;
using System.Collections.Generic;

namespace LiveKit.Rooms.Participants
{
    public class ParticipantsHub : IMutableParticipantsHub
    {
        private Participant? local;
        private readonly Dictionary<string, Participant> remoteParticipants = new();

        public event ParticipantDelegate? UpdatesFromParticipant;

        public Participant LocalParticipant()
        {
            return local
                   ?? throw new InvalidOperationException(
                       "Local participant not assigned yet"
                   );
        }

        public Participant? RemoteParticipant(string identity)
        {
            remoteParticipants.TryGetValue(identity, out var remoteParticipant);
            return remoteParticipant;
        }

        public IReadOnlyCollection<string> RemoteParticipantIdentities()
        {
            return remoteParticipants.Keys;
        }

        public void AssignLocal(Participant participant)
        {
            local = participant;
        }

        public void AddRemote(Participant participant)
        {
            remoteParticipants.Add(participant.Identity, participant);
        }

        public void RemoveRemote(Participant participant)
        {
            remoteParticipants.Remove(participant.Identity);
        }

        public void NotifyParticipantUpdate(Participant participant, UpdateFromParticipant update)
        {
            UpdatesFromParticipant?.Invoke(participant, update);
        }

        public void Clear()
        {
            local = null;
            remoteParticipants.Clear();
        }
    }
}