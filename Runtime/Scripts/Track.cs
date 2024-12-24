using System;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    public interface ITrack
    {
        string Sid { get; protected set; }
        string Name { get; }
        TrackKind Kind { get; }
        StreamState StreamState { get; }
        bool Muted { get; }
        WeakReference<Room> Room { get; }
        WeakReference<Participant> Participant { get; }
        FfiHandle TrackHandle { get; }
        
        public GetSessionStatsInstruction GetStats()
        {
            using var request = FFIBridge.Instance.NewRequest<GetStatsRequest>();
            var getStats = request.request;
            getStats.TrackHandle = (ulong)TrackHandle.DangerousGetHandle();
            using var response = request.Send();
            FfiResponse res = response;
            return new GetSessionStatsInstruction(res.GetStats.AsyncId);
        }

    }

    public interface ILocalTrack : ITrack
    {
        IRtcSource source { get; }

        public void UpdateSid (string sid) {
            Sid = sid;
        }

        public void SetMute(bool muted)
        {
            using var request = FFIBridge.Instance.NewRequest<LocalTrackMuteRequest>();
            var createTrack = request.request;
            createTrack.Mute = muted;
            createTrack.TrackHandle = (ulong)TrackHandle.DangerousGetHandle();
            using var resp = request.Send();
            FfiResponse res = resp;
            source.SetMute(muted);
        }
    }

    public interface IRemoteTrack : ITrack
    {
        public void SetEnabled(bool enabled)
        {
            using var request = FFIBridge.Instance.NewRequest<EnableRemoteTrackRequest>();
            var req = request.request;
            req.Enabled = enabled;
            req.TrackHandle = (ulong)TrackHandle.DangerousGetHandle();
            using var resp = request.Send();
            FfiResponse res = resp;
        }
    }

    public interface IAudioTrack : ITrack
    {

    }

    public interface IVideoTrack : ITrack
    {
    }

    public class Track : ITrack
    {
        private TrackInfo _info;

        public string Sid => _info.Sid;
        public string Name => _info.Name;
        public TrackKind Kind => _info.Kind;
        public StreamState StreamState => _info.StreamState;
        public bool Muted => _info.Muted;
        public WeakReference<Room> Room { internal set; get; }
        public WeakReference<Participant> Participant { get; }

        // IsOwned is true if C# owns the handle
        public bool IsOwned => Handle != null && !Handle.IsInvalid;

        public readonly FfiHandle Handle;

        FfiHandle ITrack.TrackHandle => Handle;

        string ITrack.Sid { get => _info.Sid; set => _info.Sid = value; }

        internal Track(OwnedTrack track, Room room, Participant participant)
        {
            Handle = FfiHandle.FromOwnedHandle(track.Handle);
            Room = new WeakReference<Room>(room);
            Participant = new WeakReference<Participant>(participant);
            UpdateInfo(track.Info);
        }

        internal void UpdateInfo(TrackInfo info)
        {
            _info = info;
        }

        internal void UpdateMuted(bool muted)
        {
            _info.Muted = muted;
        }
    }

    public sealed class LocalAudioTrack : Track, ILocalTrack, IAudioTrack
    {
        RtcAudioSource _source;

        IRtcSource ILocalTrack.source { get => _source; }

        internal LocalAudioTrack(OwnedTrack track, Room room, RtcAudioSource source) : base(track, room, room?.LocalParticipant) {
            _source = source;
        }

        public static LocalAudioTrack CreateAudioTrack(string name, RtcAudioSource source, Room room)
        {
            using var request = FFIBridge.Instance.NewRequest<CreateAudioTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();

            using var resp = request.Send();
            FfiResponse res = resp;
            var trackInfo = res.CreateAudioTrack.Track;
            var track = new LocalAudioTrack(trackInfo, room, source);
            return track;
        }
    }

    public sealed class LocalVideoTrack : Track, ILocalTrack, IVideoTrack
    {
        public RtcVideoSource source;

        IRtcSource ILocalTrack.source => source;

        internal LocalVideoTrack(OwnedTrack track, Room room, RtcVideoSource source) : base(track, room, room?.LocalParticipant) {
            this.source = source;
        }

        public static LocalVideoTrack CreateVideoTrack(string name, RtcVideoSource source, Room room)
        {
            using var request = FFIBridge.Instance.NewRequest<CreateVideoTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();
            using var response = request.Send();
            FfiResponse res = response;
            var trackInfo = res.CreateVideoTrack.Track;
            var track = new LocalVideoTrack(trackInfo, room, source);
            return track;
        }
    }

    public sealed class RemoteAudioTrack : Track, IRemoteTrack, IAudioTrack
    {
        internal RemoteAudioTrack(OwnedTrack track, Room room, RemoteParticipant participant) : base(track, room, participant) { }
    }

    public sealed class RemoteVideoTrack : Track, IRemoteTrack, IVideoTrack
    {
        internal RemoteVideoTrack(OwnedTrack track, Room room, RemoteParticipant participant) : base(track, room, participant) { }
    }
    
    public sealed class GetSessionStatsInstruction : YieldInstruction
    {
        private readonly ulong _asyncId;
        public RtcStats[] Stats;
        public string Error;

        internal GetSessionStatsInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.GetSessionStatsReceived += OnGetSessionStatsReceived;
        }

        private void OnGetSessionStatsReceived(GetStatsCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            Error = e.Error;
            IsError = !string.IsNullOrEmpty(Error);
            IsDone = true;
            Stats = new RtcStats[e.Stats.Count];
            for (var i = 0; i < e.Stats.Count; i++)
            {
                Stats[i] = e.Stats[i];
            }
            FfiClient.Instance.GetSessionStatsReceived -= OnGetSessionStatsReceived;
        }
    }
}