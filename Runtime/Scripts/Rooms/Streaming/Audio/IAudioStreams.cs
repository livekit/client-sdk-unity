#if !UNITY_WEBGL || UNITY_EDITOR

using RichTypes;

namespace LiveKit.Rooms.Streaming.Audio
{
    public interface IAudioStreams : IStreams<AudioStream, AudioStreamInfo>
    {
    }

    public static class AudioStreamsExtensions
    {
        public static Option<int> GetLastFrameReceivedAt(this IAudioStreams streams, StreamKey streamKey)
        {
            var weak = streams.ActiveStream(streamKey);
            return weak.Resource.Has ? weak.Resource.Value.LastFrameReceivedAt : Option<int>.None;
        }
    }
}

#endif
