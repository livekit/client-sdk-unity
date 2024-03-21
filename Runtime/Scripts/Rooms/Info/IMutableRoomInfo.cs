using LiveKit.Proto;

namespace LiveKit.Rooms.Info
{
    public interface IMutableRoomInfo : IRoomInfo
    {
        void UpdateSid(string sid);

        void UpdateName(string name);

        void UpdateMetadata(string metadata);
    }

    public static class RoomInfoExtensions
    {
        public static void UpdateFromInfo(this IMutableRoomInfo roomInfo, RoomInfo info)
        {
            roomInfo.UpdateSid(info.Sid!);
            roomInfo.UpdateName(info.Name!);
            roomInfo.UpdateMetadata(info.Metadata!);
        }
    }
}