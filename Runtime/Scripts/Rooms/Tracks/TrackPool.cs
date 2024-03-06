using UnityEngine.Pool;

namespace LiveKit.Rooms.Tracks
{
    public class TrackPool : IObjectPool<Track>
    {
        private readonly IObjectPool<Track> trackPool;

        public TrackPool()
        {
            trackPool = new ObjectPool<Track>(
                () => new Track(),
                actionOnRelease: track => track.Clear()
            );
        }

        public Track Get()
        {
            return trackPool.Get()!;
        }

        public PooledObject<Track> Get(out Track v)
        {
            return trackPool.Get(out v);
        }

        public void Release(Track element)
        {
            trackPool.Release(element);
        }

        public void Clear()
        {
            trackPool.Clear();
        }

        public int CountInactive => trackPool.CountInactive;
    }
}