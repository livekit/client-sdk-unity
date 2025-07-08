using System.Threading;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms;
using LiveKit.Rooms.TrackPublications;
using LiveKit.Rooms.Tracks;

namespace LiveKit.Rooms.AsyncInstractions
{
    public sealed class PublishTrackInstruction : AsyncInstruction
    {
        private ulong _asyncId;
        private Room _room;
        private ITrack _track;

        internal PublishTrackInstruction(ulong asyncId, Room room, ITrack track, CancellationToken token) : base(token)
        {
            _asyncId = asyncId;
            _room = room;
            _track = track;
            FfiClient.Instance.PublishTrackReceived += OnPublish;
        }

        void OnPublish(PublishTrackCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            IsError = !string.IsNullOrEmpty(e.Error);
            
            if (!IsError && e.Publication != null)
            {
                var publication = ITrackPublicationFactory.Default.NewTrackPublication(
                    e.Publication.Handle, 
                    e.Publication.Info!
                );
                
                publication.UpdateTrack(_track);
                
                _room.Participants.LocalParticipant().Publish(publication);
            }
            
            IsDone = true;

            FfiClient.Instance.PublishTrackReceived -= OnPublish;
        }
    }
}