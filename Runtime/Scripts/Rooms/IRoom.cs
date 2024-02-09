using System.Threading;
using System.Threading.Tasks;
using LiveKit.Rooms.ActiveSpeakers;
using LiveKit.Rooms.DataPipes;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Tracks.Hub;

namespace LiveKit.Rooms
{
    public interface IRoom : ITracksHub, IRoomConnectionInfo
    {
        event Room.MetaDelegate? RoomMetadataChanged;
        
        IActiveSpeakers ActiveSpeakers { get; }
        
        IParticipantsHub Participants { get; }
        
        IDataPipe DataPipe { get; }
        
        Task<bool> Connect(string url, string authToken, CancellationToken cancelToken);

        void Disconnect();
    }
}