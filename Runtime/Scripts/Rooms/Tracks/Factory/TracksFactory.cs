#if !UNITY_WEBGL || UNITY_EDITOR

using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using LiveKit.RtcSources.Video;
using UnityEngine.Pool;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit.Rooms.Tracks.Factory
{
    public class TracksFactory : ITracksFactory
    {
        private readonly IObjectPool<Track> trackPool;

        public TracksFactory() : this(new TrackPool())
        {
        }

        public TracksFactory(IObjectPool<Track> trackPool)
        {
            this.trackPool = trackPool;
        }

        public ITrack NewAudioTrack(string name, IRtcAudioSource source, IRoom room)
        {
            using var request = LiveKit.Internal.FFIClients.Requests.FFIBridge.Instance.NewRequest<CreateAudioTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.BorrowHandle().DangerousGetHandle();
            using var response = request.Send();
            return CreateTrack(response, room, true);
        }

        public ITrack NewVideoTrack(string name, RtcVideoSource source, IRoom room)
        {
            using FfiRequestWrap<CreateVideoTrackRequest>
                request = LiveKit.Internal.FFIClients.Requests.FFIBridge.Instance.NewRequest<CreateVideoTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = source.HandleId;
            using var response = request.Send();
            return CreateTrack(response, room, false);
        }

        public ITrack NewTrack(FfiHandle? handle, TrackInfo info, Room room, LKParticipant participant)
        {
            var track = trackPool.Get()!;
            track.Construct(handle, info, room, participant);
            return track;
        }

        private Track CreateTrack(FfiResponse res, IRoom room, bool isAudio)
        {
            var trackInfo = isAudio ? res.CreateAudioTrack!.Track : res.CreateVideoTrack!.Track;
            var trackHandle = IFfiHandleFactory.Default.NewFfiHandle(trackInfo!.Handle!.Id);
            var track = trackPool.Get()!;
            track.Construct(trackHandle, trackInfo.Info!, room, room.Participants.LocalParticipant());
            return track;
        }
    }
}

#endif
