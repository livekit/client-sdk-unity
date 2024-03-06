using System;
using LiveKit.Internal;
using LiveKit.Internal.FFIClients.Pools.Memory;
using LiveKit.Proto;

namespace LiveKit.Rooms.Tracks
{
    public static class LiveKitExtensions
    {
        public static void CopyTo(this OwnedBuffer buffer, Span<byte> destination)
        {
            unsafe
            {
                var unmanagedBuffer = new Span<byte>((void*)buffer.Data!.DataPtr, destination.Length);
                unmanagedBuffer.CopyTo(destination);
            }
        }
        
        public static MemoryWrap ReadAndDispose(this OwnedBuffer buffer, IMemoryPool memoryPool)
        {
            var memory = memoryPool.Memory(buffer.Data!.DataLen);
            var data = memory.Span();
            buffer.CopyTo(data);
            NativeMethods.FfiDropHandle(buffer.Handle!.Id);
            return memory;
        }
    }
}