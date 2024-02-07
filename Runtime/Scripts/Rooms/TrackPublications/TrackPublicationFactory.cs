using System.Collections.Generic;
using LiveKit.Proto;

namespace LiveKit.Rooms.TrackPublications
{
    public class TrackPublicationFactory : ITrackPublicationFactory
    {
        private readonly Stack<TrackPublication> publications = new();
        
        public TrackPublication NewTrackPublication(TrackPublicationInfo info)
        {
            lock (publications)
            {
                if (publications.TryPop(out var publication) == false)
                {
                    publication = new TrackPublication();
                }
                
                publication!.Construct(info);
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