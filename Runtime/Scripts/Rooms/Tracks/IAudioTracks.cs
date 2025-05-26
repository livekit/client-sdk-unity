using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.Tracks;

namespace LiveKit.Rooms.Tracks
{
    public interface IAudioTracks
    {
        ITrack CreateAudioTrack(string name, RtcAudioSource source);
    }
} 