using System.Threading;
using Cysharp.Threading.Tasks;
using LiveKit.Rooms.ActiveSpeakers;
using LiveKit.Rooms.DataPipes;
using LiveKit.Rooms.Info;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Tracks;
using LiveKit.Rooms.Tracks.Hub;
using LiveKit.Rooms.VideoStreaming;
using RichTypes;

#if !UNITY_WEBGL
using LiveKit.Rooms.Streaming.Audio;
#endif

namespace LiveKit.Rooms
{
    public interface IRoom : ITracksHub, IRoomConnectionInfo
    {
        event Room.MetaDelegate? RoomMetadataChanged;
 
        event Room.SidDelegate? RoomSidChanged;
 
        // Tracks and streams are not supported currently in WebGL
#if !UNITY_WEBGL
        ILocalTracks LocalTracks { get; }

        IVideoStreams VideoStreams { get; }

        IAudioStreams AudioStreams { get; }
#endif

        IRoomInfo Info { get; }
        
        IActiveSpeakers ActiveSpeakers { get; }
        
        IParticipantsHub Participants { get; }
        
        IDataPipe DataPipe { get; }
        

        void UpdateLocalMetadata(string metadata);

        void SetLocalName(string name);

        UniTask<Result> ConnectAsync(string url, string authToken, CancellationToken cancelToken, bool autoSubscribe);

        UniTask DisconnectAsync(CancellationToken cancellationToken);
    }
}
