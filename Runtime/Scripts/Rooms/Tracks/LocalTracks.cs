using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.Tracks.Factory;
using LiveKit.RtcSources.Video;

namespace LiveKit.Rooms.Tracks
{
    public class LocalTracks : ILocalTracks
    {
        private readonly ITracksFactory tracksFactory;
        private readonly IRoom room;

        public LocalTracks(ITracksFactory tracksFactory, IRoom room)
        {
            this.tracksFactory = tracksFactory;
            this.room = room;
        }

        public ITrack CreateAudioTrack(string name, IRtcAudioSource source) =>
            tracksFactory.NewAudioTrack(name, source, room);

        public ITrack CreateVideoTrack(string name, RtcVideoSource source) =>
            tracksFactory.NewVideoTrack(name, source, room);
    }
}