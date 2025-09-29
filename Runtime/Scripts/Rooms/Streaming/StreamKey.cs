using System;

namespace LiveKit.Rooms.Streaming
{
    [Serializable]
    public readonly struct StreamKey : IEquatable<StreamKey>
    {
        public readonly string identity;
        public readonly string sid;

        public StreamKey(string identity, string sid)
        {
            this.identity = identity;
            this.sid = sid;
        }

        public bool Equals(StreamKey other) =>
            identity == other.identity
            && sid == other.sid;

        public override bool Equals(object? obj)
        {
            return obj is StreamKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(identity, sid);
        }
    }
}