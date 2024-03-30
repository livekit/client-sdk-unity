using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;

namespace LiveKit
{
    public class TrackPublication
    {
        private TrackPublicationInfo _info;
        public string Sid => _info.Sid;
        public string Name => _info.Name;
        public TrackKind Kind => _info.Kind;
        public TrackSource Source => _info.Source;
        public bool Simulcasted => _info.Simulcasted;
        public uint Width => _info.Width;
        public uint Height => _info.Height;
        public string MimeType => _info.MimeType;
        public bool Muted => _info.Muted;
        public Proto.EncryptionType EncryptionType => _info.EncryptionType;

        public Track Track { private set; get; }

        protected TrackPublication(TrackPublicationInfo info)
        {
            UpdateInfo(info);
        }

        internal void UpdateInfo(TrackPublicationInfo info)
        {
            _info = info;
        }

        internal void UpdateTrack(Track track)
        {
            Track = track;
        }

        internal void UpdateMuted(bool muted)
        {
            _info.Muted = muted;
            Track?.UpdateMuted(muted);
        }
    }

    public sealed class RemoteTrackPublication : TrackPublication
    {
        public new IRemoteTrack Track => base.Track as IRemoteTrack;
        public bool Subscribed = false;

        private FfiHandle Handle;

        internal RemoteTrackPublication(TrackPublicationInfo info, FfiHandle handle) : base(info)
        {
            Handle = handle;
        }

        public void SetSubscribed(bool subscribed)
        {
            Subscribed = subscribed;
            using var request = FFIBridge.Instance.NewRequest<SetSubscribedRequest>();
            var setSubscribed = request.request;
            setSubscribed.Subscribe = subscribed;
            setSubscribed.PublicationHandle = (ulong)Handle.DangerousGetHandle();
            using var response = request.Send();
        }
    }

    public sealed class LocalTrackPublication : TrackPublication
    {
        public new ILocalTrack Track => base.Track as ILocalTrack;

        internal LocalTrackPublication(TrackPublicationInfo info) : base(info)
        {
        }
    }
}