using System;
using System.Collections.Generic;
using LiveKit.Proto;
using LiveKit.Internal;

namespace LiveKit
{
    public class TrackPublication
    {
        private TrackPublicationInfo _info;
        public string Sid => _info.Sid;
        public string Name => _info.Name;
        public TrackKind Kind => _info.Kind;
        public TrackSource Source => _info.Source;
        public bool Simulcasted => _info.Simulcasted;
        public uint Width => _info.Width;
        public uint Height => _info.Height;
        public string MimeType => _info.MimeType;
        public bool Muted => _info.Muted;
        public FfiHandle Handle {get; internal set;}

        public Track Track { private set; get; }

        protected TrackPublication(OwnedTrackPublication publication)
        {
            UpdateInfo(publication.Info);
            Handle = new FfiHandle((IntPtr)publication.Handle.Id);
        }

        internal void UpdateInfo(TrackPublicationInfo info)
        {
            _info = info;
        }

        internal void UpdateTrack(Track track)
        {
            Track = track;
        }

        internal void UpdateMuted(bool muted)
        {
            _info.Muted = muted;
            Track?.UpdateMuted(muted);
        }
    }

    public sealed class RemoteTrackPublication : TrackPublication
    {
        public new IRemoteTrack Track => base.Track as IRemoteTrack;

        internal RemoteTrackPublication(OwnedTrackPublication info) : base(info) { }
    }

    public sealed class LocalTrackPublication : TrackPublication
    {
        public new ILocalTrack Track => base.Track as ILocalTrack;

        internal LocalTrackPublication(OwnedTrackPublication info) : base(info) { }
    }
}
