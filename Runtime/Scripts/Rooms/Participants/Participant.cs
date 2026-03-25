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

#if !UNITY_WEBGL || UNITY_EDITOR
using LiveKit.Rooms.AsyncInstractions;
using LiveKit.Rooms.TrackPublications;
#endif

namespace LiveKit.Rooms.Participants
{
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
#if !UNITY_WEBGL || UNITY_EDITOR
    public class LKParticipant
    {
        public delegate void PublishDelegate(LiveKit.Rooms.TrackPublications.TrackPublication publication);

        public Origin Origin { get; private set; }

        public string Sid => info.Sid!;
        public string Identity => info.Identity!;
        public string Name => info.Name!;
        public string Metadata => info.Metadata!;
        public IReadOnlyDictionary<string, string> Attributes => info.Attributes;

        public bool Speaking { get; private set; }
        public float AudioLevel { get; private set; }

        public LKConnectionQuality ConnectionQuality { get; private set; }

        public event PublishDelegate? TrackPublished;

        public event PublishDelegate? TrackUnpublished;

        public IReadOnlyDictionary<string, LiveKit.Rooms.TrackPublications.TrackPublication> Tracks => tracks;

        internal FfiHandle Handle { get; private set; } = null!;

        public Room Room { get; private set; } = null!;

        private readonly Dictionary<string, LiveKit.Rooms.TrackPublications.TrackPublication> tracks = new();

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

        public void Publish(LiveKit.Rooms.TrackPublications.TrackPublication track)
        {
            AddTrack(track);
            TrackPublished?.Invoke(track);
        }

        public void UnPublish(string sid, out LiveKit.Rooms.TrackPublications.TrackPublication unpublishedTrack)
        {
            var publication = tracks[sid] ?? throw new Exception("Track not found");
            tracks.Remove(sid);
            TrackUnpublished?.Invoke(publication);
            unpublishedTrack = publication;
        }

        public LiveKit.Rooms.TrackPublications.TrackPublication TrackPublication(string sid)
        {
            return tracks[sid] ?? throw new Exception("Track publication not found");
        }

        public void AddTrack(LiveKit.Rooms.TrackPublications.TrackPublication track)
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

        public void UpdateQuality(LKConnectionQuality connectionQuality)
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
            LiveKit.Rooms.Tracks.ITrack localTrack,
            LiveKit.Proto.TrackPublishOptions options,
            CancellationToken token)
        {
            if (Origin is not Origin.Local)
                throw new InvalidOperationException("Can publish track for the local participant only");

            using var request = LiveKit.Internal.FFIClients.Requests.FFIBridge.Instance.NewRequest<PublishTrackRequest>();
            var publish = request.request;
            publish.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            publish.TrackHandle = (ulong)localTrack.Handle.DangerousGetHandle();
            publish.Options = options;
            using var response = request.Send();
            FfiResponse res = response;
            return new PublishTrackInstruction(res.PublishTrack.AsyncId, Room, localTrack, token);
        }

        public void UnpublishTrack(
            LiveKit.Rooms.Tracks.ITrack localTrack,
            bool stopOnUnpublish)
        {
            if (Origin is not Origin.Local)
                throw new InvalidOperationException("Can unpublish track for the local participant only");

            using var request = LiveKit.Internal.FFIClients.Requests.FFIBridge.Instance.NewRequest<UnpublishTrackRequest>();
            var publish = request.request;
            publish.LocalParticipantHandle = (ulong)Handle.DangerousGetHandle();
            publish.TrackSid = localTrack.Sid;
            publish.StopOnUnpublish = stopOnUnpublish;
            using var response = request.Send();
            FfiResponse res = response;
            LiveKit.Internal.Utils.Debug("UnpublishTrack Response:: " + res);
        }
    }
#else
    // IEquatable to keep the behaviour consistent in dictionaries for the struct type.
    public readonly struct LKParticipant : IEquatable<LKParticipant>
    {
        // Cannot guarantee participant to be presented in struct because the default init is possble
        private readonly LiveKit.Participant? jsParticipant;
        private readonly IReadOnlyDictionary<(string sid, string identity), LKConnectionQuality> qualityMap;

        // TODO Need to solve the conflict: Props must be none null, but mock props would be not valid too.
        // Underlying implementation is not null safety.
        public string Sid => jsParticipant?.Sid ?? string.Empty;
        public string Identity => jsParticipant?.Identity ?? string.Empty;
        public string Name => jsParticipant?.Name ?? string.Empty;
        public string Metadata => jsParticipant?.Metadata ?? string.Empty;

        // Official JS api misses the Quality property in Participant.
        // It needs to be preserved to read on request.
        public LKConnectionQuality ConnectionQuality
        {
            get
            {
                if (jsParticipant?.Sid != null && jsParticipant?.Identity != null)
                    if (qualityMap.TryGetValue((Sid, Identity), out LKConnectionQuality quality))
                    {
                        return quality;
                    }

                // By default quality is poor
                return LKConnectionQuality.QualityPoor;
            }
        }

        internal LKParticipant(
                LiveKit.Participant jsParticipant,
                IReadOnlyDictionary<(string sid, string identity), LKConnectionQuality> qualityMap
                )
        {
            UnityEngine.Assertions.Assert.IsNotNull(jsParticipant, "jsParticipant must not be null");

            this.jsParticipant = jsParticipant;
            this.qualityMap = qualityMap;
        }

        public bool Equals(LKParticipant other)
            => string.Equals(Sid, other.Sid, StringComparison.Ordinal);

        public override bool Equals(object obj)
            => obj is LKParticipant other && Equals(other);

        public override int GetHashCode()
            => Sid != null ? Sid.GetHashCode() : 0;

        public static bool operator ==(LKParticipant a, LKParticipant b)
            => a.Equals(b);

        public static bool operator !=(LKParticipant a, LKParticipant b)
            => !a.Equals(b);
    }
#endif
}
