using System;
using LiveKit.Proto;
using LiveKit.Internal;
using System.Threading.Tasks;
using System.Threading;

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

        internal readonly FfiHandle Handle;

        internal Track(FfiHandle handle, TrackInfo info, Room room, Participant participant)
        {
            Handle = handle;
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

        async public static Task<LocalAudioTrack> CreateAudioTrack(string name, RtcAudioSource source, Room room, CancellationTokenSource canceltoken)
        {
            var createTrack = new CreateAudioTrackRequest();
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();

            var request = new FfiRequest();
            request.CreateAudioTrack = createTrack;

            var resp = await FfiClient.SendRequest(request);
            // Check if the task has been cancelled
            if (canceltoken.IsCancellationRequested)
            {
                // End the task
                Utils.Debug("Task cancelled");
                return null;
            }
            var trackInfo = resp.CreateAudioTrack.Track;
            var trackHandle = new FfiHandle((IntPtr)trackInfo.Handle.Id);
            var track = new LocalAudioTrack(trackHandle, trackInfo.Info, room);
            return track;
        }
    }

    public sealed class LocalVideoTrack : Track, ILocalTrack, IVideoTrack
    {
        internal LocalVideoTrack(FfiHandle handle, TrackInfo info, Room room) : base(handle, info, room, room?.LocalParticipant) { }

        async public static Task<LocalVideoTrack> CreateVideoTrack(string name, RtcVideoSource source, Room room)
        {
            /*var captureOptions = new VideoCaptureOptions();
            var resolution = new VideoResolution();
            resolution.Width = 640;
            resolution.Height = 480;
            resolution.FrameRate = 30;
            captureOptions.Resolution = resolution;*/

            var createTrack = new CreateVideoTrackRequest();
            createTrack.Name = name;
            createTrack.SourceHandle = (ulong)source.Handle.DangerousGetHandle();
            //createTrack.Options = captureOptions;

            var request = new FfiRequest();
            request.CreateVideoTrack = createTrack;

            var resp = await FfiClient.SendRequest(request);
            var trackInfo = resp.CreateVideoTrack.Track;
            var trackHandle = new FfiHandle((IntPtr)trackInfo.Handle.Id);
            var track = new LocalVideoTrack(trackHandle, trackInfo.Info, room);
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
