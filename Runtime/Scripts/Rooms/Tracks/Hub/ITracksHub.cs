using LiveKit.Rooms.Participants;

#if !UNITY_WEBGL
using LiveKit.Rooms.TrackPublications;
#endif

namespace LiveKit.Rooms.Tracks.Hub
{
#if !UNITY_WEBGL
    public delegate void LocalPublishDelegate(TrackPublication publication, Participant participant);
    
    public delegate void PublishDelegate(TrackPublication publication, Participant participant);
    
    public delegate void SubscribeDelegate(ITrack track, TrackPublication publication, Participant participant);
    
    public delegate void MuteDelegate(TrackPublication publication, Participant participant);
#endif
    
    public interface ITracksHub
    {
#if !UNITY_WEBGL
        event LocalPublishDelegate? LocalTrackPublished;
        event LocalPublishDelegate? LocalTrackUnpublished;
        event PublishDelegate? TrackPublished;
        event PublishDelegate? TrackUnpublished;
        event SubscribeDelegate? TrackSubscribed;
        event SubscribeDelegate? TrackUnsubscribed;
        event MuteDelegate? TrackMuted;
        event MuteDelegate? TrackUnmuted;
#endif
    }
}

