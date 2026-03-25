using LiveKit.Proto;
using DCL.LiveKit.Public;

namespace LiveKit.Rooms.Info
{
    public interface IRoomInfo
    {
        public LKConnectionState ConnectionState { get; }
        public string Sid { get; }
        public string Name { get; }
        public string Metadata { get; }
    }
}
