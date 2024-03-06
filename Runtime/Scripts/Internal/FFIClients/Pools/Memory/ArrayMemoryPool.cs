using System.Buffers;

namespace LiveKit.Internal.FFIClients.Pools.Memory
{
    public class ArrayMemoryPool : IMemoryPool
    {
        private readonly ArrayPool<byte> arrayPool;

        public ArrayMemoryPool() : this(ArrayPool<byte>.Create()!)
        {
        }

        public ArrayMemoryPool(ArrayPool<byte> arrayPool)
        {
            this.arrayPool = arrayPool;
        }

        public MemoryWrap Memory(int byteSize)
        {
            return new MemoryWrap(arrayPool.Rent(byteSize)!, byteSize, this);
        }

        public void Release(byte[] buffer)
        {
            arrayPool.Return(buffer);
        }
    }
}