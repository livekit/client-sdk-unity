using System;
using System.Linq;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;

namespace LiveKit
{
    public class Participant
    {
        public delegate void PublishDelegate(RemoteTrackPublication publication);

        private ParticipantInfo _info;
        public String Sid => _info.Sid;
        public String Identity => _info.Identity;
        public String Name => _info.Name;
        public String Metadata => _info.Metadata;

        public bool Speaking { private set; get; }
        public float AudioLevel { private set; get; }
        public ConnectionQuality ConnectionQuality { private set; get; }

        public event PublishDelegate TrackPublished;
        public event PublishDelegate TrackUnpublished;

        internal readonly Dictionary<String, TrackPublication> _tracks = new();
        public IReadOnlyDictionary<String, TrackPublication> Tracks => _tracks;

        protected Participant(ParticipantInfo info)
        {
            UpdateInfo(info);
        }

        internal void UpdateInfo(ParticipantInfo info)
        {
            _info = info;
        }

        internal void OnTrackPublished(RemoteTrackPublication publication)
        {
            TrackPublished?.Invoke(publication);
        }

        internal void OnTrackUnpublished(RemoteTrackPublication publication)
        {
            TrackUnpublished?.Invoke(publication);
        }

        public void PublishData()
        {

        }
    }

    public sealed class LocalParticipant : Participant
    {
        public new IReadOnlyDictionary<String, LocalTrackPublication> Tracks =>
            base.Tracks.ToDictionary(p => p.Key, p => (LocalTrackPublication)p.Value);

        internal LocalParticipant(ParticipantInfo info) : base(info) { }
    }

    public sealed class RemoteParticipant : Participant
    {
        public new IReadOnlyDictionary<String, RemoteTrackPublication> Tracks =>
            base.Tracks.ToDictionary(p => p.Key, p => (RemoteTrackPublication)p.Value);

        internal RemoteParticipant(ParticipantInfo info) : base(info) { }
    }
}
