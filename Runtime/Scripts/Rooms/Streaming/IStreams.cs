using System;
using System.Collections.Generic;
using RichTypes;

namespace LiveKit.Rooms.Streaming
{
    public interface IStreams<TStream, TInfo> : IDisposable where TStream : class
    {
        /// <returns>Caller doesn't care about disposing the Stream, returns null if stream is not found</returns>
        Weak<TStream> ActiveStream(StreamKey streamKey);

        bool Release(StreamKey streamKey);

        void Free();

        void ListInfo(List<StreamInfo<TInfo>> output);

        /// <summary>
        /// Internal
        /// </summary>
        void AssignRoom(Room room);
    }
}