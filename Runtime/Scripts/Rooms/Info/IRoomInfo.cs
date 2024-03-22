using LiveKit.Proto;

namespace LiveKit.Rooms.Info
{
    public interface IRoomInfo
    {
        public ConnectionState ConnectionState { get; }
        public string Sid { get; }
        public string Name { get; }
        public string Metadata { get; }
    }
}