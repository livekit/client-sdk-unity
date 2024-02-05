using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Threading;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Rooms.AsyncInstractions;
using LiveKit.Rooms.Tracks;

namespace LiveKit.Rooms.Participants
{
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class Participant
    {
        public delegate void PublishDelegate(TrackPublication publication);
        
        public Origin Origin { get; }
        
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
        
        internal FfiHandle Handle { get; }

        public Room Room { get; }

        private readonly Dictionary<string, TrackPublication> tracks = new();
        private readonly ParticipantInfo info;

        public Participant(ParticipantInfo info, Room room, FfiHandle handle, Origin origin)
        {
            Room = room;
            Origin = origin;
            Handle = handle;
            this.info = info;
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

        public static Participant NewRemote(
            Room room,
            ParticipantInfo info, 
            IReadOnlyList<OwnedTrackPublication>? publications, 
            FfiHandle handle
            )
        {
            var participant = new Participant(info, room, handle, Origin.Remote);
            foreach (var pubInfo in publications ?? Array.Empty<OwnedTrackPublication>())
            {
                var publication = new TrackPublication(pubInfo.Info!);
                participant.AddTrack(publication);
                publication.SetSubscribedForRemote(true);
            }

            return participant;
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