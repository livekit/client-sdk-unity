using System;
using System.Threading;
using LiveKit.Proto;
using LiveKit.Rooms.AsyncInstractions;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms
{
    public interface IRoom
    {
        event Room.MetaDelegate? RoomMetadataChanged;
        event Room.ParticipantDelegate? ParticipantConnected;
        event Room.ParticipantDelegate? ParticipantMetadataChanged;
        event Room.ParticipantDelegate? ParticipantDisconnected;
        event Room.LocalPublishDelegate? LocalTrackPublished;
        event Room.LocalPublishDelegate? LocalTrackUnpublished;
        event Room.PublishDelegate? TrackPublished;
        event Room.PublishDelegate? TrackUnpublished;
        event Room.SubscribeDelegate? TrackSubscribed;
        event Room.SubscribeDelegate? TrackUnsubscribed;
        event Room.MuteDelegate? TrackMuted;
        event Room.MuteDelegate? TrackUnmuted;
        event Room.SpeakersChangeDelegate? ActiveSpeakersChanged;
        event Room.ConnectionQualityChangeDelegate? ConnectionQualityChanged;
        event Room.DataDelegate? DataReceived;
        event Room.ConnectionStateChangeDelegate? ConnectionStateChanged;
        event Room.ConnectionDelegate? Connected;
        event Room.ConnectionDelegate? Disconnected;
        event Room.ConnectionDelegate? Reconnecting;
        event Room.ConnectionDelegate? Reconnected;
        
        IParticipantsHub Participants { get; }
        
        //TODO async
        ConnectInstruction Connect(string url, string authToken, CancellationToken cancelToken);
        
        void PublishData(Span<byte> data, string topic, DataPacketKind kind = DataPacketKind.KindLossy);

        void Disconnect();
    }
}