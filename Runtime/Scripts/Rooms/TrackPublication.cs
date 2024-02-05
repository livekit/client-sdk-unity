using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;

namespace LiveKit.Rooms
{
    public class TrackPublication
    {
        private readonly TrackPublicationInfo info;

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

        public Track? Track { get; private set; }

        public TrackPublication(TrackPublicationInfo info)
        {
            this.info = info;
        }

        internal void UpdateTrack(Track track)
        {
            Track = track;
        }

        public void RemoveTrack(out Track? removedTrack)
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
            setSubscribed.PublicationHandle = (ulong)Track.Handle.DangerousGetHandle();
            using var response = request.Send();
        }
    }

}