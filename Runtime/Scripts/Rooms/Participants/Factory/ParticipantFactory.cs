using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit.Rooms.Participants.Factory
{
    public class ParticipantFactory : IParticipantFactory
    {
        private readonly Stack<Participant> participants = new();

        public Participant NewParticipant(ParticipantInfo info, Room room, FfiHandle handle, Origin origin)
        {
            lock (participants)
            {
                if (participants.TryPop(out var participant) == false)
                {
                    participant = new Participant();
                }

                participant!.Construct(info, room, handle, origin);
                return participant;
            }
        }

        public void Release(Participant participant)
        {
            lock (participants)
            {
                participant.Clear();
                participants.Push(participant);
            }
        }
    }
}