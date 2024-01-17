using System;
using System.Linq;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;

namespace LiveKit
{
    public class Participant
    {
        public delegate void PublishDelegate(RemoteTrackPublication publication);

        private ParticipantInfo _info;
        public string Sid => _info.Sid;
        public string Identity => _info.Identity;
        public string Name => _info.Name;
        public string Metadata => _info.Metadata;

        internal FfiHandle Handle;

        public bool Speaking { private set; get; }
        public float AudioLevel { private set; get; }
        public ConnectionQuality ConnectionQuality { private set; get; }

        public event PublishDelegate TrackPublished;
        public event PublishDelegate TrackUnpublished;

        private Room _room;
        public Room Room
        {
            get
            {
                return _room;
            }
        }

        public IReadOnlyDictionary<string, TrackPublication> Tracks => _tracks;
        internal readonly Dictionary<string, TrackPublication> _tracks = new();

        protected Participant(ParticipantInfo info, Room room, FfiHandle handle)
        {
            _room =  room;
            Handle = handle;
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

    }

    public sealed class LocalParticipant : Participant
    {
        public new IReadOnlyDictionary<string, LocalTrackPublication> Tracks =>
            base.Tracks.ToDictionary(p => p.Key, p => (LocalTrackPublication)p.Value);

        internal LocalParticipant(ParticipantInfo info, Room room, FfiHandle handle) : base(info, room, handle) { }

        async public Task<PublishTrackInstruction> PublishTrack(ILocalTrack localTrack, TrackPublishOptions options, CancellationToken canceltoken)
        {
            if (Room == null)
                throw new Exception("room is invalid");

            if (canceltoken.IsCancellationRequested) return null;

            var track = (Track)localTrack;
            var publish = new PublishTrackRequest();
            publish.LocalParticipantHandle = (ulong)Room.LocalParticipant.Handle.DangerousGetHandle();
            publish.TrackHandle =   (ulong)track.Handle.DangerousGetHandle();
            publish.Options = options;

            var request = new FfiRequest();
            request.PublishTrack = publish;
            var resp = await FfiClient.SendRequest(request);

            if (canceltoken.IsCancellationRequested) return null;

            if (resp!=null)
                return new PublishTrackInstruction(resp.PublishTrack.AsyncId);
            return null;
        }
    }

    public sealed class RemoteParticipant : Participant
    {
        public new IReadOnlyDictionary<string, RemoteTrackPublication> Tracks =>
            base.Tracks.ToDictionary(p => p.Key, p => (RemoteTrackPublication)p.Value);

        internal RemoteParticipant(ParticipantInfo info, Room room, FfiHandle handle) : base(info, room, handle) { }
    }

    public sealed class PublishTrackInstruction 
    {
        public bool IsDone { protected set; get; }
        public bool IsError { protected set; get; }

        private ulong _asyncId;

        internal PublishTrackInstruction(ulong asyncId)
        {
            _asyncId = asyncId;
            FfiClient.Instance.PublishTrackReceived += OnPublish;
        }

        void OnPublish(PublishTrackCallback e)
        {
            if (e.AsyncId != _asyncId)
                return;

            IsError = !string.IsNullOrEmpty(e.Error);
            IsDone = true;
            FfiClient.Instance.PublishTrackReceived -= OnPublish;
        }
    }
}
