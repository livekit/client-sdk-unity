#if !UNITY_WEBGL || UNITY_EDITOR

using System;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using DCL.LiveKit.Public;

namespace LiveKit.Rooms.DataPipes
{
    public class DataPipe : IMutableDataPipe
    {
        private IParticipantsHub participantsHub = null!;

        public event ReceivedDataDelegate? DataReceived;

        public void PublishData(
            Span<byte> data,
            string topic,
            IReadOnlyCollection<string> identities,
            LKDataPacketKind kind = LKDataPacketKind.KindLossy
        )
        {
            unsafe
            {
                fixed (byte* pointer = data)
                {
                    PublishData(pointer, data.Length, topic, identities, kind);
                }
            }
        }

        private unsafe void PublishData(
            byte* data,
            int len,
            string topic,
            IReadOnlyCollection<string> identities,
            LKDataPacketKind kind = LKDataPacketKind.KindLossy
        )
        {
            using var request = FFIBridge.Instance.NewRequest<PublishDataRequest>();
            var dataRequest = request.request;
            dataRequest.DestinationIdentities!.Clear();
            dataRequest.DestinationIdentities.AddRange(identities);
            dataRequest.DataLen = (ulong)len;
            dataRequest.DataPtr = (ulong)data;
            dataRequest.Reliable = LKDataPacketKindUtils.ToProto(kind) == global::LiveKit.Proto.DataPacketKind.KindReliable;
            dataRequest.Topic = topic;
            dataRequest.LocalParticipantHandle = (ulong)participantsHub.LocalParticipant().Handle.DangerousGetHandle();
            LiveKit.Internal.Utils.Debug("Sending message: " + topic);
            using var response = request.Send();
        }

        public void Assign(IParticipantsHub participants)
        {
            participantsHub = participants;
        }

        public void Notify(ReadOnlySpan<byte> data, LKParticipant participant, string topic, LKDataPacketKind kind)
        {
            DataReceived?.Invoke(data, participant, topic, kind);
        }
    }
}

#endif
