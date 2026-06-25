using Google.Protobuf;

namespace LiveKit.Internal.FFIClients.Pools.Memory
{
    public interface IMemoryPool
    {
        MemoryWrap Memory(int byteSize);

        void Release(byte[] buffer);
    }

    public static class MemoryPoolExtensions
    {
        public static MemoryWrap Memory(this IMemoryPool pool, IMessage forMessage)
        {
            var size = forMessage.CalculateSize();
            return pool.Memory(size);
        }
    }
}