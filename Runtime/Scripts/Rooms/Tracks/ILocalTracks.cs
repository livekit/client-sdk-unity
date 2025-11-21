using LiveKit.RtcSources.Video;

namespace LiveKit.Rooms.Tracks
{
    public interface ILocalTracks
    {
        ITrack CreateAudioTrack(string name, IRtcAudioSource source);

        ITrack CreateVideoTrack(string name, RtcVideoSource source);
    }
} 