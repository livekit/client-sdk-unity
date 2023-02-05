using System;
using LiveKit.Proto;

namespace LiveKit
{
    public interface ITrack
    {
        public String Sid { get; }
        public String Name { get; }
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
        public VideoSink Sink { get; }
    }

    public class Track : ITrack
    {
        private TrackInfo _info;

        public String Sid => _info.Sid;
        public String Name => _info.Name;
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
        public VideoSink Sink { private set; get; }

        internal RemoteVideoTrack(TrackInfo info) : base(info) { }

        internal void UpdateSink(VideoSink sink)
        {
            Sink = sink;
        }
    }
}
