#if !UNITY_WEBGL || UNITY_EDITOR

namespace LiveKit.Rooms.Streaming.Audio
{
    public interface IAudioStreams : IStreams<AudioStream, AudioStreamInfo>
    {
        
    }
}

#endif
