using System;
using LiveKit.Proto;

namespace LiveKit
{
    public interface ITrack
    {
        public string Sid { get; }
        public string Name { get; }
        public TrackKind Kind { get; }
        public StreamState StreamState { get; }
        public bool Muted { get; }
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

        internal Track(TrackInfo info)
        {
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
        internal LocalAudioTrack(TrackInfo info) : base(info) { }
    }

    public sealed class LocalVideoTrack : Track, ILocalTrack, IVideoTrack
    {
        public VideoSink Sink { private set; get; }

        internal LocalVideoTrack(TrackInfo info) : base(info) { }
    }

    public sealed class RemoteAudioTrack : Track, IRemoteTrack, IAudioTrack
    {
        internal RemoteAudioTrack(TrackInfo info) : base(info) { }
    }

    public sealed class RemoteVideoTrack : Track, IRemoteTrack, IVideoTrack
    {
        internal RemoteVideoTrack(TrackInfo info) : base(info) { }
    }
}
