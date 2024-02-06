using System;

namespace LiveKit.Internal.FFIClients.Pools.Memory
{
    public readonly struct MemoryWrap : IDisposable
    {
        private readonly byte[] buffer;
        private readonly int length;
        private readonly IMemoryPool memoryPool;

        public int Length => length;

        public MemoryWrap(byte[] buffer, int length, IMemoryPool memoryPool)
        {
            this.buffer = buffer;
            this.length = length;
            this.memoryPool = memoryPool;
        }

        public Span<byte> Span()
        {
            return new Span<byte>(buffer, 0, length);
        }

        public void Dispose()
        {
            memoryPool.Release(buffer);
        }
    }
}