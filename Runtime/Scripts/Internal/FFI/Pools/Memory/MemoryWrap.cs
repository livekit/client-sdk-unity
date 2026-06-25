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

        /// <summary>
        /// Gives the direct access to the buffer. Ownership remains on MemoryWrap and it can dispose it anytime.
        /// You know what you are doing
        /// </summary>
        public byte[] DangerousBuffer()
        {
            return buffer;
        }

        public void Dispose()
        {
            memoryPool.Release(buffer);
        }
    }
}