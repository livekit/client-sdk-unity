#if UNITY_WEBGL

using Cysharp.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using DCL.LiveKit.Public;
using LiveKit.Rooms.Participants;

using JsRoom = LiveKit.Room;

namespace LiveKit.Rooms.DataPipes
{
    public class JsDataPipe : IDataPipe
    {
        // For some reason topic is not available to receive but can be sent in WebGL version
        private const string EMPTY_TOPIC = "";

        // Won't crash on Js due threading (it doesn't use that actually)
        private static System.Buffers.ArrayPool<byte> POOL = System.Buffers.ArrayPool<byte>.Shared;

        private readonly JsRoom room;

        public event ReceivedDataDelegate DataReceived;

        public JsDataPipe(JsRoom room)
        {
            this.room = room;
            room.DataReceived += (byte[] data, RemoteParticipant participant, DataPacketKind? kind) =>
            {
                LKParticipant wrap = new LKParticipant(participant);
                LKDataPacketKind packetKind = kind switch
                {
                    DataPacketKind.LOSSY => LKDataPacketKind.KindLossy,
                    DataPacketKind.RELIABLE => LKDataPacketKind.KindReliable,
                    null => LKDataPacketKind.KindLossy, // by default treat the kind as lossy
                };
                DataReceived?.Invoke(data, wrap, EMPTY_TOPIC, packetKind);
            };
        }

        public void PublishData(
                Span<byte> data,
                string topic,
                IReadOnlyCollection<string> destinationSids,
                LKDataPacketKind kind = LKDataPacketKind.KindLossy
                )
        {
            byte[] buffer = POOL.Rent(data.Length);
            data.CopyTo(buffer);
            PublichAsync(buffer, data.Length, topic, destinationSids, kind).Forget();
        }

        private async UniTaskVoid PublichAsync(
                byte[] buffer,
                int size,
                string topic,
                IReadOnlyCollection<string> destinationSids,
                LKDataPacketKind kind
                )
        {
            LocalParticipant self = room.LocalParticipant;
            await self.PublishData(
                    buffer,
                    offset: 0,
                    size,
                    reliable: kind is LKDataPacketKind.KindReliable,
                    destinationIdentities: destinationSids.ToArray(), // TODO: Yes, it's not optimised, but the underlying Api provides only array access
                    topic
                    );
            POOL.Return(buffer);
        }

   
    }
}

#endif





