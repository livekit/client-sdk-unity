using System;
using System.Collections.Generic;
using LiveKit.Proto;

namespace LiveKit
{
    public class TrackPublication
    {
        private TrackPublicationInfo _info;
        public String Sid => _info.Sid;
        public String Name => _info.Name;
        public TrackKind Kind => _info.Kind;
        public TrackSource Source => _info.Source;
        public bool Simulcasted => _info.Simulcasted;
        public Dimension Dimension => _info.Dimension;
        public String MimeType => _info.MimeType;
        public bool Muted => _info.Muted;

        public Track Track { private set; get; }

        protected TrackPublication(TrackPublicationInfo info)
        {
            UpdateInfo(info);
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

        internal RemoteTrackPublication(TrackPublicationInfo info) : base(info) { }
    }

    public sealed class LocalTrackPublication : TrackPublication
    {
        public new ILocalTrack Track => base.Track as ILocalTrack;

        internal LocalTrackPublication(TrackPublicationInfo info) : base(info) { }
    }
}
