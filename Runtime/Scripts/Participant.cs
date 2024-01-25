using System;
using System.Linq;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using UnityEngine;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    public class Participant
    {
        public delegate void PublishDelegate(RemoteTrackPublication publication);

        public FfiOwnedHandle Handle;
        private ParticipantInfo _info;
        public string Sid => _info.Sid;
        public string Identity => _info.Identity;
        public string Name => _info.Name;
        public string Metadata => _info.Metadata;

        public void SetMeta(string meta)
        {
            _info.Metadata = meta;
        }

        public bool Speaking { private set; get; }
        public float AudioLevel { private set; get; }
        public ConnectionQuality ConnectionQuality { private set; get; }

        public event PublishDelegate TrackPublished;
        public event PublishDelegate TrackUnpublished;

        public readonly WeakReference<Room> Room;
        public IReadOnlyDictionary<string, TrackPublication> Tracks => _tracks;
        internal readonly Dictionary<string, TrackPublication> _tracks = new();

        protected Participant(OwnedParticipant participant, Room room)
        {
            Room = new WeakReference<Room>(room);
            Handle = participant.Handle;
            UpdateInfo(participant.Info);
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

        internal LocalParticipant(OwnedParticipant participant, Room room) : base(participant, room) { }

        public PublishTrackInstruction PublishTrack(ILocalTrack localTrack, TrackPublishOptions options)
        {
            if (!Room.TryGetTarget(out var room))
                throw new Exception("room is invalid");

            var track = (Track)localTrack;            
            
            using var request = FFIBridge.Instance.NewRequest<PublishTrackRequest>();
            var publish = request.request;
            publish.LocalParticipantHandle = (ulong)Handle.Id;
            publish.TrackHandle = (ulong)track.Handle.Id;
            publish.Options = options;
            using var response = request.Send();
            FfiResponse res = response;
            return new PublishTrackInstruction(res.PublishTrack.AsyncId);
        }

        public PublishTrackInstruction publishData(byte[] data, string[] destination_sids = null, bool reliable = true, string topic = null)
        {
            if (!Room.TryGetTarget(out var room))
                throw new Exception("room is invalid");

            using var request = FFIBridge.Instance.NewRequest<PublishDataRequest>();

            var publish = request.request;
            publish.LocalParticipantHandle = Handle.Id;
            publish.Kind = reliable ? DataPacketKind.KindReliable : DataPacketKind.KindLossy;

            if (destination_sids is not null)
            {
                publish.DestinationSids.AddRange(destination_sids);
            }

            if(topic is not null)
            {
                publish.Topic = topic;
            }

            unsafe {
                publish.DataLen = (ulong)data.Length;
                publish.DataPtr = (ulong)System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement<byte>(data, 0);
            }

            var response = request.Send();
            FfiResponse resp = response;

            return new PublishTrackInstruction(resp.PublishData.AsyncId);
        }


        public void UpdateMetadata(string metadata)
        {
            using var request = FFIBridge.Instance.NewRequest<UpdateLocalMetadataRequest>();
            var updateReq = request.request;
            updateReq.Metadata = metadata;
            var resp = request.Send();
        }

        public void UpdateName(string name)
        {
            using var request = FFIBridge.Instance.NewRequest<UpdateLocalNameRequest>();
            var updateReq = request.request;
            updateReq.Name = name;
            var resp = request.Send();
        }
    }

    public sealed class RemoteParticipant : Participant
    {
        public new IReadOnlyDictionary<string, RemoteTrackPublication> Tracks =>
            base.Tracks.ToDictionary(p => p.Key, p => (RemoteTrackPublication)p.Value);

        internal RemoteParticipant(OwnedParticipant participant, Room room) : base(participant, room) { }
    }

    public sealed class PublishTrackInstruction : YieldInstruction
    {
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
