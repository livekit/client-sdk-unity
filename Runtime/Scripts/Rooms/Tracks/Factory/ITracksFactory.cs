using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms.Tracks.Factory
{
    public interface ITracksFactory
    {
        ITrack NewAudioTrack(string name, RtcAudioSource source, Room room);

        ITrack NewLocalTrack(string name, RtcVideoSource source, Room room);

        ITrack NewTrack(FfiHandle? handle, TrackInfo info, Room room, Participant participant);
    }
}