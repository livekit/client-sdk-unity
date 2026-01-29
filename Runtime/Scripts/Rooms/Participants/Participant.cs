using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Threading;
using Google.Protobuf.Collections;
using LiveKit.Rooms.Tracks;
using UnityEngine;
using DCL.LiveKit.Public;

#if !UNITY_WEBGL
using LiveKit.Rooms.AsyncInstractions;
using LiveKit.Rooms.TrackPublications;
#endif

namespace LiveKit.Rooms.Participants
{
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class LKParticipant
#if !UNITY_WEBGL
    {
        public delegate void PublishDelegate(TrackPublication publication);

        public Origin Origin { get; private set; }

        public string Sid => info.Sid!;
        public string Identity => info.Identity!;
        public string Name => info.Name!;
        public string Metadata => info.Metadata!;
        public IReadOnlyDictionary<string, string> Attributes => info.Attributes;

        public bool Speaking { get; private set; }
        public float AudioLevel { get; private set; }

        public ConnectionQuality ConnectionQuality { get; private set; }

        public event PublishDelegate? TrackPublished;

        public event PublishDelegate? TrackUnpublished;

        public IReadOnlyDictionary<string, TrackPublication> Tracks => tracks;

        internal FfiHandle Handle { get; private set; } = null!;

        public Room Room { get; private set; } = null!;

        private readonly Dictionary<string, TrackPublication> tracks = new();

        private ParticipantInfo info = null!;

        internal void Construct(ParticipantInfo info, Room room, FfiHandle handle, Origin origin)
        {
            Room = room;
            Origin = origin;
            Handle = handle;
            this.info = info;
        }

        public void Clear()
        {
            Room = null!;
            Handle = null!;
            info = null!;
            tracks.Clear();
        }

        public void Publish(TrackPublication track)
        {
            AddTrack(track);
            TrackPublished?.Invoke(track);
        }

        public void UnPublish(string sid, out TrackPublication unpublishedTrack)
        {
            var publication = tracks[sid] ?? throw new Exception("Track not found");
            tracks.Remove(sid);
            TrackUnpublished?.Invoke(publication);
            unpublishedTrack = publication;
        }

        public TrackPublication TrackPublication(string sid)
        {
            return tracks[sid] ?? throw new Exception("Track publication not found");
        }

        public void AddTrack(TrackPublication track)
        {
            tracks.Add(track.Sid, track);
        }

        public void UpdateMeta(string meta)
        {
            info.Metadata = meta;
        }

        public void UpdateName(string name)
        {
            info.Name = name;
        }

        public void UpdateQuality(ConnectionQuality connectionQuality)
        {
            ConnectionQuality = connectionQuality;
        }

        public void UpdateAttributes(RepeatedField<AttributesEntry> attributes)
        {
            info.Attributes.Clear();
            foreach (var pair in attributes)
            {
                if (pair is { HasKey: true, HasValue: true })
                    info.Attributes.Add(pair.Key, pair.Value);
            }
        }

        public PublishTrackInstruction PublishTrack(
            ITrack localTrack,
            TrackPublishOptions options,
            CancellationToken token)
        {
            if (Origin is not Origin.Local)
                throw new InvalidOperationException("Can publish track for the local participant only");

            using var request = FFIBridge.Instance.NewRequest<PublishTrackRequest>();
            var publish = request.request;
            publish.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            publish.TrackHandle = (ulong)localTrack.Handle.DangerousGetHandle();
            publish.Options = options;
            using var response = request.Send();
            FfiResponse res = response;
            return new PublishTrackInstruction(res.PublishTrack.AsyncId, Room, localTrack, token);
        }

        public void UnpublishTrack(
            ITrack localTrack,
            bool stopOnUnpublish)
        {
            if (Origin is not Origin.Local)
                throw new InvalidOperationException("Can unpublish track for the local participant only");

            using var request = FFIBridge.Instance.NewRequest<UnpublishTrackRequest>();
            var publish = request.request;
            publish.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            publish.TrackSid = localTrack.Sid;
            publish.StopOnUnpublish = stopOnUnpublish;
            using var response = request.Send();
            FfiResponse res = response;
            Utils.Debug("UnpublishTrack Response:: " + res);
        }
    }
#else
//TODO and replace for WebGL
    {
        public string Sid => throw new System.NotImplementedException();
        public string Identity => throw new System.NotImplementedException();
        public string Name => throw new System.NotImplementedException();
        public string Metadata => throw new System.NotImplementedException();

        public LKConnectionQuality ConnectionQuality => throw new System.NotImplementedException();

        public void Clear()
        {
            // TODO
        }
    }
#endif

}
