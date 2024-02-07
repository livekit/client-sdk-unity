using System;
using System.Buffers;
using LiveKit.Internal.FFIClients.Pools.Memory;

namespace LiveKit.Internal
{
    // Basic RingBuffer implementation (used WebRtc_RingBuffer as reference)
    // The one from com.unity.collections is dealing element per element, which is not efficient when dealing with bytes
    public class RingBuffer : IDisposable
    {
        private MemoryWrap _buffer;
        private int _writePos;
        private int _readPos;
        private bool _sameWrap;

        private static readonly IMemoryPool MemoryPool = new ArrayMemoryPool(ArrayPool<byte>.Shared!);

        public RingBuffer(int size)
        {
            _buffer = MemoryPool.Memory(size);
            _sameWrap = true;
        }

        public int Write(ReadOnlySpan<byte> data)
        {
            int free = AvailableWrite();
            int write = (free < data.Length ? free : data.Length);
            int n = write;
            int margin = _buffer.Length - _writePos;

            if (write > margin)
            {
                data.Slice(0, margin).CopyTo(_buffer.Span().Slice(_writePos));
                _writePos = 0;
                n -= margin;
                _sameWrap = false;
            }
            data.Slice(write - n, n).CopyTo(_buffer.Span().Slice(_writePos));
            _writePos += n;
            return write;
        }

        public int Read(Span<byte> data)
        {
            int readCount = GetBufferReadRegions(data.Length, out int dataIndex1, out int dataLen1, out int dataIndex2, out int dataLen2);
            if (dataLen2 > 0)
            {
                _buffer.Span().Slice(dataIndex1, dataLen1).CopyTo(data);
                _buffer.Span().Slice(dataIndex2, dataLen2).CopyTo(data.Slice(dataLen1));
            }
            else
            {
                // TODO(theomonnom): Don't always copy in this case?
                _buffer.Span().Slice(dataIndex1, dataLen1).CopyTo(data);
            }

            MoveReadPtr(readCount);
            return readCount;
        }

        private int MoveReadPtr(int len)
        {
            int free = AvailableWrite();
            int read = AvailableRead();
            int readPos = _readPos;

            if (len > read)
                len = read;

            if (len < -free)
                len = -free;

            readPos += len;
            if (readPos > _buffer.Length)
            {
                readPos -= _buffer.Length;
                _sameWrap = true;
            }
            if (readPos < 0)
            {
                readPos += _buffer.Length;
                _sameWrap = false;
            }

            _readPos = readPos;
            return len;
        }

        private int GetBufferReadRegions(int len, out int dataIndex1, out int dataLen1, out int dataIndex2, out int dataLen2)
        {
            int readable = AvailableRead();
            int read = readable < len ? readable : len;
            int margin = _buffer.Length - _readPos;

            if (read > margin)
            {
                // Not contiguous
                dataIndex1 = _readPos;
                dataLen1 = margin;
                dataIndex2 = 0;
                dataLen2 = read - margin;
            }
            else
            {
                // Contiguous
                dataIndex1 = _readPos;
                dataLen1 = read;
                dataIndex2 = 0;
                dataLen2 = 0;
            }

            return read;
        }

        public int AvailableRead()
        {
            if (_sameWrap)
                return _writePos - _readPos;
            else
                return _buffer.Length - _readPos + _writePos;
        }

        public int AvailableWrite()
        {
            return _buffer.Length - AvailableRead();
        }

        public void Dispose()
        {
            _buffer.Dispose();
        }
    }
}
