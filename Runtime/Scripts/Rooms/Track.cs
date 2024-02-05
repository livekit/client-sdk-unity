using System;
using LiveKit.Proto;
using LiveKit.Internal;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit.Rooms
{
    public interface ITrack
    {
        Origin Origin { get; }
        string Sid { get; }
        string Name { get; }
        TrackKind Kind { get; }
        StreamState StreamState { get; }
        bool Muted { get; }
        WeakReference<Room> Room { get; }
        WeakReference<Participant> Participant { get; }
        FfiHandle? Handle { get; }
    }

    public class Track : ITrack
    {
        private readonly TrackInfo info;

        public Origin Origin => info.Remote ? Origin.Remote : Origin.Local;
        public string Sid => info.Sid!;
        public string Name => info.Name!;
        public TrackKind Kind => info.Kind;
        public StreamState StreamState => info.StreamState;
        public bool Muted => info.Muted;
        public WeakReference<Room> Room { get; }
        public WeakReference<Participant> Participant { get; }

        // IsOwned is true if C# owns the handle
        public bool IsOwned => Handle is { IsInvalid: false };

        public FfiHandle? Handle => handle;

        private readonly FfiHandle? handle;
        private CancellationToken token;

        internal Track(FfiHandle? handle, TrackInfo info, Room room, Participant participant)
        {
            Room = new WeakReference<Room>(room);
            Participant = new WeakReference<Participant>(participant);
            this.handle = handle;
            this.info = info;
        }

        internal void UpdateMuted(bool muted)
        {
            info.Muted = muted;
        }

        public static Track CreateAudioTrack(string name, RtcAudioSource source, Room room)
        {
            using var request = FFIBridge.Instance.NewRequest<CreateAudioTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();
            using var response = request.Send();
            return CreateTrack(response, room);
        }

        public static Track CreateLocalTrack(string name, RtcVideoSource source, Room room)
        {
            using var request = FFIBridge.Instance.NewRequest<CreateVideoTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();
            using var response = request.Send();
            return CreateTrack(response, room);
        }

        private static Track CreateTrack(FfiResponse res, Room room)
        {
            var trackInfo = res.CreateVideoTrack!.Track;
            var trackHandle = new FfiHandle((IntPtr)trackInfo!.Handle.Id);
            var track = new Track(trackHandle, trackInfo.Info, room, room.LocalParticipant);
            return track;
        }
    }
}