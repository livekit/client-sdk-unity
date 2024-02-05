using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Rooms.AsyncInstractions;
using LiveKit.Rooms.TrackPublications;
using LiveKit.Rooms.Tracks;

namespace LiveKit.Rooms.Participants
{
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class Participant
    {
        public delegate void PublishDelegate(TrackPublication publication);
        
        public Origin Origin { get; private set; }
        
        public string Sid => info.Sid!;
        public string Identity => info.Identity!;
        public string Name => info.Name!;
        public string Metadata => info.Metadata!;
        
        public bool Speaking { get; private set; }

        public float AudioLevel { get; private set; }

        public ConnectionQuality ConnectionQuality { get; private set; }

        public event PublishDelegate? TrackPublished;

        public event PublishDelegate? TrackUnpublished;
        
        public IReadOnlyDictionary<string, TrackPublication> Tracks => tracks;
        
        internal FfiHandle Handle { get; private set; }= null!;

        public Room Room { get; private set; }= null!;

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
            return new PublishTrackInstruction(res.PublishTrack.AsyncId, Room, token);
        }
    }
}