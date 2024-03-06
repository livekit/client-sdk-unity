using LiveKit.Rooms.Participants;
using LiveKit.Rooms.TrackPublications;

namespace LiveKit.Rooms.Tracks.Hub
{
    public delegate void LocalPublishDelegate(TrackPublication publication, Participant participant);
    
    public delegate void PublishDelegate(TrackPublication publication, Participant participant);
    
    public delegate void SubscribeDelegate(ITrack track, TrackPublication publication, Participant participant);
    
    public delegate void MuteDelegate(TrackPublication publication, Participant participant);
    
    public interface ITracksHub
    {
        event LocalPublishDelegate? LocalTrackPublished;
        event LocalPublishDelegate? LocalTrackUnpublished;
        event PublishDelegate? TrackPublished;
        event PublishDelegate? TrackUnpublished;
        event SubscribeDelegate? TrackSubscribed;
        event SubscribeDelegate? TrackUnsubscribed;
        event MuteDelegate? TrackMuted;
        event MuteDelegate? TrackUnmuted;
    }
}