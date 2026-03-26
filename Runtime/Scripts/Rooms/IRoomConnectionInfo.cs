using LiveKit.Proto;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms
{
    public delegate void ConnectionQualityChangeDelegate(ConnectionQuality quality, Participant participant);


    public delegate void ConnectionStateChangeDelegate(ConnectionState connectionState);


    public delegate void ConnectionDelegate(IRoom room, ConnectionUpdate connectionUpdate, DisconnectReason? disconnectReason = null);


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