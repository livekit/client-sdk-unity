using System.Collections.Generic;
using LiveKit.Proto;

namespace LiveKit.Rooms.TrackPublications
{
    public class TrackPublicationFactory : ITrackPublicationFactory
    {
        private readonly Stack<TrackPublication> publications = new();
        
        public TrackPublication NewTrackPublication(FfiOwnedHandle handle, TrackPublicationInfo info)
        {
            lock (publications)
            {
                if (publications.TryPop(out var publication) == false)
                {
                    publication = new TrackPublication();
                }
                
                publication!.Construct(handle, info);
                return publication;
            }
        }
        
        public void Release(TrackPublication publication)
        {
            lock (publications)
            {
                publication.Clear();
                publications.Push(publication);
            }
        }
    }
}