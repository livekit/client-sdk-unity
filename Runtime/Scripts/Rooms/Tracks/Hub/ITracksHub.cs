using LiveKit.Rooms.Participants;

#if !UNITY_WEBGL || UNITY_EDITOR
using LiveKit.Rooms.TrackPublications;
#endif

namespace LiveKit.Rooms.Tracks.Hub
{
#if !UNITY_WEBGL || UNITY_EDITOR
    public delegate void LocalPublishDelegate(LiveKit.Rooms.TrackPublications.TrackPublication publication, LKParticipant participant);
    
    public delegate void PublishDelegate(LiveKit.Rooms.TrackPublications.TrackPublication publication, LKParticipant participant);
    
    public delegate void SubscribeDelegate(ITrack track, LiveKit.Rooms.TrackPublications.TrackPublication publication, LKParticipant participant);
    
    public delegate void MuteDelegate(LiveKit.Rooms.TrackPublications.TrackPublication publication, LKParticipant participant);
#endif
    
    public interface ITracksHub
    {
#if !UNITY_WEBGL || UNITY_EDITOR
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

