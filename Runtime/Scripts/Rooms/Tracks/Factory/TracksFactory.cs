using System;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using UnityEngine.Pool;

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

        public ITrack NewAudioTrack(string name, RtcAudioSource source, Room room)
        {
            using var request = FFIBridge.Instance.NewRequest<CreateAudioTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();
            using var response = request.Send();
            return CreateTrack(response, room);
        }

        public ITrack NewLocalTrack(string name, RtcVideoSource source, Room room)
        {
            using var request = FFIBridge.Instance.NewRequest<CreateVideoTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();
            using var response = request.Send();
            return CreateTrack(response, room);
        }

        public ITrack NewTrack(FfiHandle? handle, TrackInfo info, Room room, Participant participant)
        {
            var track = trackPool.Get()!;
            track.Construct(handle, info, room, participant);
            return track;
        }

        private Track CreateTrack(FfiResponse res, Room room)
        {
            var trackInfo = res.CreateVideoTrack!.Track;
            var trackHandle = IFfiHandleFactory.Default.NewFfiHandle(trackInfo!.Handle!.Id);
            var track = trackPool.Get()!;
            track.Construct(trackHandle, trackInfo.Info!, room, room.Participants.LocalParticipant());
            return track;
        }
    }
}