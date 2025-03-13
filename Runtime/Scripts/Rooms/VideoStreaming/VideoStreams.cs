using System;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms.VideoStreaming
{
    public class VideoStreams : IVideoStreams
    {
        [Serializable]
        private readonly struct Key : IEquatable<Key>
        {
            public readonly string identity;
            public readonly string sid;

            public Key(string identity, string sid)
            {
                this.identity = identity;
                this.sid = sid;
            }

            public bool Equals(Key other) =>
                identity == other.identity
                && sid == other.sid;

            public override bool Equals(object? obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(identity, sid);
            }
        }

        private readonly IParticipantsHub participantsHub;
        private readonly Dictionary<Key, IVideoStream> videoStreams = new();

        public VideoStreams(IParticipantsHub participantsHub)
        {
            this.participantsHub = participantsHub;
        }

        public WeakReference<IVideoStream>? VideoStream(string identity, string sid)
        {
            lock (this)
            {
                var key = new Key(identity, sid);
                if (videoStreams.TryGetValue(key, out var videoStream) == false)
                {
                    var participant = participantsHub.RemoteParticipant(identity);
                    if (participant == null)
                        return null;

                    if (participant.Tracks.TryGetValue(sid, out var trackPublication) == false)
                        return null;

                    if (trackPublication!.Track == null || trackPublication.Track!.Kind is not TrackKind.KindVideo)
                        return null;

                    var videoTrack = trackPublication.Track!;

                    using var request = FFIBridge.Instance.NewRequest<NewVideoStreamRequest>();
                    var newVideoStream = request.request;
                    newVideoStream.TrackHandle = (ulong)videoTrack.Handle.DangerousGetHandle();
                    newVideoStream.Format = VideoBufferType.Rgba;
                    newVideoStream.NormalizeStride = true;
                    newVideoStream.Type = VideoStreamType.VideoStreamNative;
                    using var response = request.Send();
                    FfiResponse res = response;

                    var streamInfo = res.NewVideoStream!.Stream;
                    videoStreams[key] = videoStream = new VideoStream(streamInfo!);
                }

                return new WeakReference<IVideoStream>(videoStream);
            }
        }

        public void Free()
        {
            lock (this)
            {
                foreach (var videoStream in videoStreams.Values) videoStream.Dispose();
                videoStreams.Clear();
            }
        }
    }
}