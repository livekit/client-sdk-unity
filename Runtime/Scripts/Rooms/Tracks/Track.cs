#if !UNITY_WEBGL || UNITY_EDITOR

using System;
using LiveKit.Proto;
using LiveKit.Internal;
using System.Threading;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms.Tracks
{
    public interface ITrack
    {
        Origin Origin { get; }
        string Sid { get; }
        string Name { get; }
        LiveKit.Proto.TrackKind Kind { get; }
        StreamState StreamState { get; }
        bool Muted { get; }
        WeakReference<IRoom> Room { get; }
        WeakReference<LKParticipant> Participant { get; }
        FfiHandle? Handle { get; }
        
        void UpdateMuted(bool muted);
    }

    public class Track : ITrack
    {
        private TrackInfo info;
        private CancellationToken token;

        public Origin Origin => info.Remote ? Origin.Remote : Origin.Local;
        public string Sid => info.Sid!;
        public string Name => info.Name!;
        public LiveKit.Proto.TrackKind Kind => info.Kind;
        public StreamState StreamState => info.StreamState;
        public bool Muted => info.Muted;
        // IsOwned is true if C# owns the handle
        public bool IsOwned => Handle is { IsInvalid: false };

        public WeakReference<IRoom> Room { get; private set; }
        public WeakReference<LKParticipant> Participant { get; private set;}
        public FfiHandle? Handle { get; private set; }

        public void Construct(FfiHandle? handle, TrackInfo info, IRoom room, LKParticipant participant)
        {
            Room = new WeakReference<IRoom>(room);
            Participant = new WeakReference<LKParticipant>(participant);
            Handle = handle;
            this.info = info;
        }

        public void Clear()
        {
            Room = null!;
            Participant = null!;
            Handle = null;
            info = null!;
        }

        public void UpdateMuted(bool muted)
        {
            info.Muted = muted;
        }
    }
}

#endif
