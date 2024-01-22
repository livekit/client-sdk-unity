using System;
using LiveKit.Proto;
using LiveKit.Internal;

namespace LiveKit
{
    public interface ITrack
    {
        string Sid { get; }
        string Name { get; }
        TrackKind Kind { get; }
        StreamState StreamState { get; }
        bool Muted { get; }
        WeakReference<Room> Room { get; }
        WeakReference<Participant> Participant { get; }
        FfiHandle Handle { get; }

    }

    public interface ILocalTrack : ITrack
    {

    }

    public interface IRemoteTrack : ITrack
    {

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
        public WeakReference<Room> Room { get; }
        public WeakReference<Participant> Participant { get; }

        public FfiHandle Handle { get; internal set; }

        // IsOwned is true if C# owns the handle
        public bool IsOwned => Handle != null && !Handle.IsInvalid;

        internal Track(OwnedTrack ownedInfo)
        {
            Handle = new FfiHandle((IntPtr) ownedInfo.Handle.Id);
            UpdateInfo(ownedInfo.Info);
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
        internal LocalAudioTrack(OwnedTrack ownedInfo) : base(ownedInfo) { }

        public static LocalAudioTrack CreateAudioTrack(string name, RtcAudioSource source)
        {
            var createTrack = new CreateAudioTrackRequest();
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();

            var request = new FfiRequest();
            request.CreateAudioTrack = createTrack;

            var resp = FfiClient.SendRequest(request);
            var track = new LocalAudioTrack(resp.CreateAudioTrack.Track);
            return track;
        }
    }

    public sealed class LocalVideoTrack : Track, ILocalTrack, IVideoTrack
    {
        internal LocalVideoTrack(OwnedTrack ownedInfo) : base(ownedInfo) { }

        public static LocalVideoTrack CreateVideoTrack(string name, RtcVideoSource source)
        {
            var createTrack = new CreateVideoTrackRequest();
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();

            var request = new FfiRequest();
            request.CreateVideoTrack = createTrack;

            var resp = FfiClient.SendRequest(request);
            var trackInfo = resp.CreateVideoTrack.Track;
            var track = new LocalVideoTrack(trackInfo);
            return track;
        }
    }

    public sealed class RemoteAudioTrack : Track, IRemoteTrack, IAudioTrack
    {
        internal RemoteAudioTrack(OwnedTrack ownedInfo) : base(ownedInfo) { }
    }

    public sealed class RemoteVideoTrack : Track, IRemoteTrack, IVideoTrack
    {
        internal RemoteVideoTrack(OwnedTrack ownedInfo) : base(ownedInfo) { }
    }
}
