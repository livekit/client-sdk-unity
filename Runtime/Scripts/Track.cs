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
            var instruction = new GetSessionStatsInstruction(request.RequestAsyncId);
            using var response = request.Send();
            return instruction;
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

        public FfiHandle Handle { get; private set; }

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

        // Replaces the underlying FFI track handle. Used when a local track is rebuilt because its
        // audio source recreated its native handle at a new sample rate/channel count. Disposes
        // the previous handle.
        internal void SwapHandle(OwnedTrack track)
        {
            var previous = Handle;
            Handle = FfiHandle.FromOwnedHandle(track.Handle);
            UpdateInfo(track.Info);
            previous?.Dispose();
        }

        internal void UpdateMuted(bool muted)
        {
            _info.Muted = muted;
        }

        internal void DisposeHandles()
        {
            Handle?.Dispose();
        }
    }

    public sealed class LocalAudioTrack : Track, ILocalTrack, IAudioTrack
    {
        RtcAudioSource _source;
        string _name;
        LocalParticipant _participant;
        TrackPublishOptions _publishOptions;

        IRtcSource ILocalTrack.source { get => _source; }

        internal LocalAudioTrack(OwnedTrack track, Room room, RtcAudioSource source) : base(track, room, room?.LocalParticipant) {
            _source = source;
        }

        public static LocalAudioTrack CreateAudioTrack(string name, RtcAudioSource source, Room room)
        {
            var track = new LocalAudioTrack(CreateFfiTrack(name, source), room, source);
            track._name = name;
            // The track is bound to a specific native source handle at creation time and cannot
            // follow a new one in place. If the source recreates its native handle at runtime
            // (e.g. on a sample-rate change), rebuild and republish the track onto the new handle.
            source.NativeSourceChanged += track.OnNativeSourceChanged;
            return track;
        }

        private static OwnedTrack CreateFfiTrack(string name, RtcAudioSource source)
        {
            using var request = FFIBridge.Instance.NewRequest<CreateAudioTrackRequest>();
            var createTrack = request.request;
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();

            using var resp = request.Send();
            FfiResponse res = resp;
            return res.CreateAudioTrack.Track;
        }

        // Records the publish target so the track can republish itself after a source recreation.
        internal void RememberPublishTarget(LocalParticipant participant, TrackPublishOptions options)
        {
            _participant = participant;
            _publishOptions = options;
        }

        // Runs on the main thread after the source recreated its native handle. Rebuilds the FFI
        // track onto the new source and, if the track was already published, republishes it.
        private void OnNativeSourceChanged()
        {
            var wasPublished = _participant != null && !string.IsNullOrEmpty(Sid);

            // Unpublish first (reads the current Sid) before swapping to the new handle.
            if (wasPublished)
                _participant.UnpublishTrack(this, false);

            SwapHandle(CreateFfiTrack(_name, _source));

            if (wasPublished)
                _participant.PublishTrack(this, _publishOptions);
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
            // This waiter is a one-shot response; cancellation and completion race through the
            // same pending entry, so only one path can finish the instruction.
            FfiClient.Instance.RegisterPendingCallback(asyncId, static e => e.GetStats, OnGetSessionStatsReceived, OnCanceled);
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
        }

        private void OnCanceled()
        {
            Error = "Canceled";
            IsError = true;
            IsDone = true;
        }
    }
}
