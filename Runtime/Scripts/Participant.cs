using System;
using System.Linq;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

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

            var publish = new PublishTrackRequest();
            publish.LocalParticipantHandle = Handle.Id;
            publish.TrackHandle = (ulong)track.TrackHandle;
            publish.Options = options;

            var request = new FfiRequest();
            request.PublishTrack = publish;

            var resp = FfiClient.SendRequest(request);
            return new PublishTrackInstruction(resp.PublishTrack.AsyncId);
        }

        public PublishTrackInstruction publishData(byte[] data, string[] destination_sids = null, bool reliable = true, string topic = null)
        {
            if (!Room.TryGetTarget(out var room))
                throw new Exception("room is invalid");

            var publish = new PublishDataRequest();
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

            var request = new FfiRequest();
            request.PublishData = publish;

            var resp = FfiClient.SendRequest(request);

            return new PublishTrackInstruction(resp.PublishTrack.AsyncId);
        }


        public void UpdateMetadata(string metadata)
        {
            var updateReq = new UpdateLocalMetadataRequest();
            updateReq.Metadata = metadata;
            var request = new FfiRequest();
            request.UpdateLocalMetadata = updateReq;

            FfiClient.SendRequest(request);
        }

        public void UpdateName(string name)
        {
            var updateReq = new UpdateLocalNameRequest();
            updateReq.Name = name;
            var request = new FfiRequest();
            request.UpdateLocalName = updateReq;

            FfiClient.SendRequest(request);
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
