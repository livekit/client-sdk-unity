using System;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.TrackPublications;

namespace LiveKit.Rooms.Participants.Factory
{
    public interface IParticipantFactory
    {
        Participant NewParticipant(ParticipantInfo info, Room room, FfiHandle handle, Origin origin);

        void Release(Participant participant);

        static readonly IParticipantFactory Default = new ParticipantFactory();
    }

    public static class ParticipantFactoryExtension
    {
        public static Participant NewRemote(
            this IParticipantFactory factory,
            Room room,
            ParticipantInfo info,
            IReadOnlyList<OwnedTrackPublication>? publications,
            FfiHandle handle
        )
        {
            var participant = factory.NewParticipant(info, room, handle, Origin.Remote);
                foreach (var pubInfo in publications ?? Array.Empty<OwnedTrackPublication>())
                {
              
                    var publication = ITrackPublicationFactory.Default.NewTrackPublication(pubInfo.Handle, pubInfo.Info!);
                    participant.AddTrack(publication);
                }

            return participant;
        }
    }
}