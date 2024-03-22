using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rooms.ActiveSpeakers;
using LiveKit.Rooms.DataPipes;
using LiveKit.Rooms.Info;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Tracks.Hub;

namespace LiveKit.Rooms
{
    public interface IRoom : ITracksHub, IRoomConnectionInfo
    {
        event Room.MetaDelegate? RoomMetadataChanged;
        
        IRoomInfo Info { get; }
        
        IActiveSpeakers ActiveSpeakers { get; }
        
        IParticipantsHub Participants { get; }
        
        IDataPipe DataPipe { get; }

        void UpdateLocalMetadata(string metadata);

        Task<bool> ConnectAsync(string url, string authToken, CancellationToken cancelToken, bool autoSubscribe);

        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}