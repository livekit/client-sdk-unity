using System;
using System.Collections.Generic;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using DCL.LiveKit.Public;

namespace LiveKit.Rooms.DataPipes
{
    public delegate void ReceivedDataDelegate(
        ReadOnlySpan<byte> data,
        LKParticipant participant,
        string topic,
        LKDataPacketKind kind
    );

    public interface IDataPipe
    {
        event ReceivedDataDelegate DataReceived;

        void PublishData(
            Span<byte> data,
            string topic,
            IReadOnlyCollection<string> destinationSids,
            LKDataPacketKind kind = LKDataPacketKind.KindLossy
        );
    }
    
    public interface IMutableDataPipe : IDataPipe
    {
        void Assign(IParticipantsHub participants);
        
        void Notify(
                ReadOnlySpan<byte> data, 
                LKParticipant participant, 
                string topic, 
                LKDataPacketKind kind
                );
    }
}
