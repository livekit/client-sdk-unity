using System;
using LiveKit.Proto;
using LiveKit.Internal;
using UnityEngine;

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
        ulong TrackHandle { get; }
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

        // IsOwned is true if C# owns the handle
        public bool IsOwned => Handle != null && !Handle.IsInvalid;

        public readonly FfiHandle Handle;

        public ulong TrackHandle { get; }

        internal Track(FfiHandle handle, TrackInfo info, Room room, Participant participant)
        {
            Handle = handle;
            TrackHandle = (ulong)handle.DangerousGetHandle();
            Room = new WeakReference<Room>(room);
            Participant = new WeakReference<Participant>(participant);
            UpdateInfo(info);
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
        internal LocalAudioTrack(FfiHandle handle, TrackInfo info, Room room) : base(handle, info, room, room?.LocalParticipant) { }

        public static LocalAudioTrack CreateAudioTrack(string name, RtcAudioSource source)
        {
            var createTrack = new CreateAudioTrackRequest();
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();

            var request = new FfiRequest();
            request.CreateAudioTrack = createTrack;

            var resp = FfiClient.SendRequest(request);
            var newTrack = resp.CreateAudioTrack.Track;
            var trackHandle = new FfiHandle((IntPtr)newTrack.Handle.Id);
            var track = new LocalAudioTrack(trackHandle, newTrack.Info, null);
            return track;
        }
    }

    public sealed class LocalVideoTrack : Track, ILocalTrack, IVideoTrack
    {
        internal LocalVideoTrack(FfiHandle handle, TrackInfo info, Room room) : base(handle, info, room, room?.LocalParticipant) { }

        public static LocalVideoTrack CreateVideoTrack(string name, RtcVideoSource source)
        {

            var createTrack = new CreateVideoTrackRequest();
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();
  
            var request = new FfiRequest();
            request.CreateVideoTrack = createTrack;

            var resp = FfiClient.SendRequest(request);
            var newTrack = resp.CreateVideoTrack.Track;
            var trackHandle = new FfiHandle((IntPtr)newTrack.Handle.Id);
            var track = new LocalVideoTrack(trackHandle, newTrack.Info, null);
            return track;
        }
    }

    public sealed class RemoteAudioTrack : Track, IRemoteTrack, IAudioTrack
    {
        internal RemoteAudioTrack(FfiHandle handle, TrackInfo info, Room room, RemoteParticipant participant) : base(handle, info, room, participant) { }
    }

    public sealed class RemoteVideoTrack : Track, IRemoteTrack, IVideoTrack
    {
        internal RemoteVideoTrack(FfiHandle handle, TrackInfo info, Room room, RemoteParticipant participant) : base(handle, info, room, participant) { }
    }
}
