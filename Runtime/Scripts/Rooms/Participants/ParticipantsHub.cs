using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LiveKit.Rooms.Participants
{
    public class ParticipantsHub : IMutableParticipantsHub
    {
        private readonly ConcurrentDictionary<string, Participant> remoteParticipants = new();
        private Participant? local;

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

        /// <summary>
        ///     Don't expose ConcurrentDictionary.Keys as it creates a whole new collection on get
        /// </summary>
        public IReadOnlyDictionary<string, Participant> RemoteParticipantIdentities()
        {
            return remoteParticipants;
        }

        public void AssignLocal(Participant participant)
        {
            local = participant;
        }

        public void AddRemote(Participant participant)
        {
            remoteParticipants[participant.Identity] = participant;
        }

        public void RemoveRemote(Participant participant)
        {
            remoteParticipants.TryRemove(participant.Identity, out _);
        }

        public void NotifyParticipantUpdate(Participant participant, UpdateFromParticipant update)
        {
            UpdatesFromParticipant?.Invoke(participant, update);
        }

        public void Clear()
        {
            local?.Clear();
            local = null;
            foreach (var participant in remoteParticipants.Values)
            {
                participant.Clear();
            }

            remoteParticipants.Clear();
        }
    }
}