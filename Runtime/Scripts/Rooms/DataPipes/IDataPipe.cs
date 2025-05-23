using System;
using System.Collections.Generic;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms.DataPipes
{
    public delegate void ReceivedDataDelegate(
        ReadOnlySpan<byte> data,
        Participant participant,
        string topic,
        DataPacketKind kind
    );

    public interface IDataPipe
    {
        event ReceivedDataDelegate DataReceived;

        void PublishData(
            Span<byte> data,
            string topic,
            IReadOnlyCollection<string> destinationSids,
            DataPacketKind kind = DataPacketKind.KindLossy
        );
    }
    
    public interface IMutableDataPipe : IDataPipe
    {
        void Assign(IParticipantsHub participants);
        
        void Notify(ReadOnlySpan<byte> data, Participant participant, string topic, DataPacketKind kind);
    }
}