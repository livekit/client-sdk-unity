using System;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Tracks;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class AudioStreams : Streams<IAudioStream>, IAudioStreams
    {
        public AudioStreams(IParticipantsHub participantsHub) : base(participantsHub, TrackKind.KindAudio)
        {
        }

        protected override IAudioStream NewStreamInstance(ITrack track)
        {
            using var request = FFIBridge.Instance.NewRequest<NewAudioStreamRequest>();
            var newStream = request.request;
            newStream.TrackHandle = (ulong)track.Handle!.DangerousGetHandle();
            newStream.Type = AudioStreamType.AudioStreamNative;
            using var response = request.Send();
            FfiResponse res = response;

            var streamInfo = res.NewAudioStream!.Stream;
            return new AudioStream(this, streamInfo!);
        }
    }
}