using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Tracks;

namespace LiveKit.Rooms.TrackPublications
{
    public class TrackPublication
    {
        private TrackPublicationInfo info;

        public Origin Origin => info.Remote ? Origin.Remote : Origin.Local;
        public string Sid => info.Sid!;
        public string Name => info.Name!;
        public TrackKind Kind => info.Kind;
        public TrackSource Source => info.Source;
        public bool Simulcasted => info.Simulcasted;
        public uint Width => info.Width;
        public uint Height => info.Height;
        public string MimeType => info.MimeType!;
        public bool Muted => info.Muted;

        public ITrack? Track { get; private set; }
        public FfiOwnedHandle? Handle { get; private set; }

        internal void Construct(FfiOwnedHandle handle, TrackPublicationInfo info)
        {
            this.info = info;
            this.Handle = handle;
        }

        public void Clear()
        {
            info = null!;
            Track = null;
        }

        internal void UpdateTrack(ITrack track)
        {
            Track = track;
        }

        public void RemoveTrack(out ITrack? removedTrack)
        {
            removedTrack = Track;
            Track = null;
        }

        internal void UpdateMuted(bool muted)
        {
            info.Muted = muted;
            Track?.UpdateMuted(muted);
        }
        
        public void SetSubscribedForRemote(bool subscribed)
        {
            if (Origin is not Origin.Remote)
            {
                throw new System.InvalidOperationException("Cannot set subscribed on non-remote track");
            }
            using var request = FFIBridge.Instance.NewRequest<SetSubscribedRequest>();
            var setSubscribed = request.request;
            setSubscribed.Subscribe = subscribed;
            setSubscribed.PublicationHandle = Handle.Id;
            using var response = request.Send();
        }
    }

}