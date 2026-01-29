using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LiveKit.Rooms.Participants
{
    public class ParticipantsHub : IMutableParticipantsHub
    {
        private readonly ConcurrentDictionary<string, LKParticipant> remoteParticipants = new();
        private LKParticipant? local;

        public event ParticipantDelegate? UpdatesFromParticipant;

        public LKParticipant LocalParticipant()
        {
            return local
                   ?? throw new InvalidOperationException(
                       "Local participant not assigned yet"
                   );
        }

        public LKParticipant? RemoteParticipant(string identity)
        {
            remoteParticipants.TryGetValue(identity, out var remoteParticipant);
            return remoteParticipant;
        }

        /// <summary>
        ///     Don't expose ConcurrentDictionary.Keys as it creates a whole new collection on get
        /// </summary>
        public IReadOnlyDictionary<string, LKParticipant> RemoteParticipantIdentities()
        {
            return remoteParticipants;
        }

        public void AssignLocal(LKParticipant participant)
        {
            local = participant;
        }

        public void AddRemote(LKParticipant participant)
        {
            remoteParticipants[participant.Identity] = participant;
        }

        public void RemoveRemote(LKParticipant participant)
        {
            remoteParticipants.TryRemove(participant.Identity, out _);
        }

        public void NotifyParticipantUpdate(LKParticipant participant, UpdateFromParticipant update)
        {
            UpdatesFromParticipant?.Invoke(participant, update);
        }

        public void Clear()
        {
#if !UNITY_WEBGL
            local?.Clear();
#endif

            local = null;

#if !UNITY_WEBGL
            foreach (var participant in remoteParticipants.Values)
            {
                participant.Clear();
            }
#endif

            remoteParticipants.Clear();
        }
    }
}
