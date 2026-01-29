#if !UNITY_WEBGL

using LiveKit.Audio;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Tracks;

namespace LiveKit.Rooms.Streaming.Audio
{
    public class AudioStreams : Streams<AudioStream, AudioStreamInfo>, IAudioStreams
    {
        public AudioStreams(IParticipantsHub participantsHub) : base(participantsHub, TrackKind.KindAudio)
        {
        }

        protected override AudioStream NewStreamInstance(StreamKey streamKey, ITrack track)
        {
            return new AudioStream(streamKey, track);
        }

        protected override AudioStreamInfo InfoFromStream(AudioStream stream)
        {
            return stream.AudioStreamInfo;
        }
    }
}

#endif
