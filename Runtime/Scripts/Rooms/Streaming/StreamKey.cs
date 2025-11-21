using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace LiveKit.Rooms.Streaming
{
    [Serializable]
    public readonly struct StreamKey : IEquatable<StreamKey>
    {
        public readonly string identity;
        public readonly string sid;

        public StreamKey(string identity, string sid)
        {
            this.identity = identity;
            this.sid = sid;
        }

        public bool Equals(StreamKey other) =>
            identity == other.identity
            && sid == other.sid;

        public override bool Equals(object? obj)
        {
            return obj is StreamKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(identity, sid);
        }

        public override string ToString()
        {
            return $"{{ identity: {identity}, sid: {sid} }}";
        }
    }

    public static class StreamKeyUtils
    {
        private static long fileIndexAtomic;
        
        public static readonly string RecordsDirectory = Path.Combine(Application.persistentDataPath!, "livekit_audio_wav");
        
        public static string NewPersistentFilePathByName(string name)
        {
            long index = Interlocked.Increment(ref fileIndexAtomic);
            string fileName = $"name__{name}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_n{index}.wav";
            return Path.Combine(RecordsDirectory, fileName);
        }

        public static string NewPersistentFilePathByStreamKey(StreamKey key, string postfix)
        {
            long index = Interlocked.Increment(ref fileIndexAtomic);
            string fileName = $"stream__{key.identity}_{key.sid}_{postfix}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_n{index}.wav";
            return Path.Combine(RecordsDirectory, fileName);
        }
    }
}