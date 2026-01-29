using LiveKit.Rooms.Participants;
using DCL.LiveKit.Public;

namespace LiveKit.Rooms
{
    public delegate void ConnectionQualityChangeDelegate(LKConnectionQuality quality, LKParticipant participant);


    public delegate void ConnectionStateChangeDelegate(LKConnectionState connectionState);


    public delegate void ConnectionDelegate(IRoom room, ConnectionUpdate connectionUpdate, LKDisconnectReason? disconnectReason = null);


    public enum ConnectionUpdate
    {
        Connected,
        Disconnected,
        Reconnecting,
        Reconnected
    }

    public interface IRoomConnectionInfo
    {
        event ConnectionQualityChangeDelegate? ConnectionQualityChanged;

        event ConnectionStateChangeDelegate? ConnectionStateChanged;

        event ConnectionDelegate? ConnectionUpdated;
    }
}
