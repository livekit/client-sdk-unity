#if !UNITY_WEBGL || UNITY_EDITOR

using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms.Participants.Factory
{
    public class ParticipantFactory : IParticipantFactory
    {
        private readonly Stack<LKParticipant> participants = new();

        public LKParticipant NewParticipant(ParticipantInfo info, Room room, FfiHandle handle, Origin origin)
        {
            lock (participants)
            {
                if (participants.TryPop(out var participant) == false)
                {
                    participant = new LKParticipant();
                }

                participant!.Construct(info, room, handle, origin);
                return participant;
            }
        }

        public void Release(LKParticipant participant)
        {
            lock (participants)
            {
                participant.Clear();
                participants.Push(participant);
            }
        }
    }
}

#endif
