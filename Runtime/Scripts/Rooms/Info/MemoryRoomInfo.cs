using LiveKit.Proto;
using DCL.LiveKit.Public;

namespace LiveKit.Rooms.Info
{
    public class MemoryRoomInfo : IMutableRoomInfo
    {
        public LKConnectionState ConnectionState { get; private set; }
        public string Sid { get; private set; }
        public string Name { get; private set; }
        public string Metadata { get; private set; }

        public void UpdateConnectionState(LKConnectionState state)
        {
            ConnectionState = state;
        }

        public void UpdateSid(string sid)
        {
            Sid = sid;
        }

        public void UpdateName(string name)
        {
            Name = name;
        }

        public void UpdateMetadata(string metadata)
        {
            Metadata = metadata;
        }
    }
}
