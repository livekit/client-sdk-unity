using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.Tracks.Factory;

namespace LiveKit.Rooms.Tracks
{
    public class AudioTracks : IAudioTracks
    {
        private readonly ITracksFactory tracksFactory;
        private readonly IRoom room;

        public AudioTracks(ITracksFactory tracksFactory, IRoom room)
        {
            this.tracksFactory = tracksFactory;
            this.room = room;
        }

        public ITrack CreateAudioTrack(string name, RtcAudioSource source) =>
            tracksFactory.NewAudioTrack(name, source, room);
    }
} 