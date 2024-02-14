using System;
using System.Buffers;
using LiveKit.Internal.FFIClients.Pools.Memory;

namespace LiveKit.Internal
{
    // Basic RingBuffer implementation (used WebRtc_RingBuffer as reference)
    // The one from com.unity.collections is dealing element per element, which is not efficient when dealing with bytes
    public struct RingBuffer : IDisposable
    {
        private readonly MemoryWrap buffer;
        private int writePos;
        private int readPos;
        private bool sameWrap;

        private static readonly IMemoryPool MemoryPool = new ArrayMemoryPool(ArrayPool<byte>.Shared!);

        public RingBuffer(int size)
        {
            buffer = MemoryPool.Memory(size);
            writePos = 0;
            readPos = 0;
            sameWrap = true;
        }

        public int Write(ReadOnlySpan<byte> data)
        {
            int free = AvailableWrite();
            int write = (free < data.Length ? free : data.Length);
            int n = write;
            int margin = buffer.Length - writePos;

            if (write > margin)
            {
                data.Slice(0, margin).CopyTo(buffer.Span().Slice(writePos));
                writePos = 0;
                n -= margin;
                sameWrap = false;
            }
            data.Slice(write - n, n).CopyTo(buffer.Span().Slice(writePos));
            writePos += n;
            return write;
        }

        public int Read(Span<byte> data)
        {
            int readCount = GetBufferReadRegions(data.Length, out int dataIndex1, out int dataLen1, out int dataIndex2, out int dataLen2);
            if (dataLen2 > 0)
            {
                buffer.Span().Slice(dataIndex1, dataLen1).CopyTo(data);
                buffer.Span().Slice(dataIndex2, dataLen2).CopyTo(data.Slice(dataLen1));
            }
            else
            {
                // TODO(theomonnom): Don't always copy in this case?
                buffer.Span().Slice(dataIndex1, dataLen1).CopyTo(data);
            }

            MoveReadPtr(readCount);
            return readCount;
        }

        private int MoveReadPtr(int len)
        {
            int free = AvailableWrite();
            int read = AvailableRead();
            int readPosition = readPos;

            if (len > read)
                len = read;

            if (len < -free)
                len = -free;

            readPosition += len;
            if (readPosition > buffer.Length)
            {
                readPosition -= buffer.Length;
                sameWrap = true;
            }
            if (readPosition < 0)
            {
                readPosition += buffer.Length;
                sameWrap = false;
            }

            readPos = readPosition;
            return len;
        }

        private int GetBufferReadRegions(int len, out int dataIndex1, out int dataLen1, out int dataIndex2, out int dataLen2)
        {
            int readable = AvailableRead();
            int read = readable < len ? readable : len;
            int margin = buffer.Length - readPos;

            if (read > margin)
            {
                // Not contiguous
                dataIndex1 = readPos;
                dataLen1 = margin;
                dataIndex2 = 0;
                dataLen2 = read - margin;
            }
            else
            {
                // Contiguous
                dataIndex1 = readPos;
                dataLen1 = read;
                dataIndex2 = 0;
                dataLen2 = 0;
            }

            return read;
        }

        public int AvailableRead()
        {
            return sameWrap 
                ? writePos - readPos 
                : buffer.Length - readPos + writePos;
        }

        public int AvailableWrite()
        {
            return buffer.Length - AvailableRead();
        }

        public void Dispose()
        {
            buffer.Dispose();
        }
    }
}
