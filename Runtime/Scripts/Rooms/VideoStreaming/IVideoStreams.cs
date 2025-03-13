using System;

namespace LiveKit.Rooms.VideoStreaming
{
    public interface IVideoStreams
    {
        /// <returns>Caller doesn't care about disposing the IVideoStream, returns null if stream is not found</returns>
        WeakReference<IVideoStream>? VideoStream(string identity, string sid);
    }
}