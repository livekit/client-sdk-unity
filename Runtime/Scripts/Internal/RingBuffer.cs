using System;

namespace LiveKit.Internal
{
    // Basic RingBuffer implementation (used WebRtc_RingBuffer as reference)
    // The one from com.unity.collections is dealing element per element, which is not efficient when dealing with bytes
    public class RingBuffer
    {
        private byte[] _buffer;
        private int _writePos;
        private int _readPos;
        private bool _sameWrap;

        public RingBuffer(int size)
        {
            _buffer = new byte[size];
            _sameWrap = true;
        }

        public int Write(byte[] data, int offset, int count)
        {
            /*int free = AvailableWrite();
            int write = (free < count ? free : count);
            int n = write;
            int margin = _buffer.Length - _writePos;

            if (write > margin)
            {
                Buffer.BlockCopy(data, offset, _buffer, _writePos, margin);
                _writePos = 0;
                n -= margin;
                _sameWrap = false;
            }
            Buffer.BlockCopy(data, offset + write - n, _buffer, _writePos, n);
            _writePos += n;*/

            return Write(data.AsSpan(offset, count));
        }

        public int Write(ReadOnlySpan<byte> data)
        {
            int free = AvailableWrite();
            int write = (free < data.Length ? free : data.Length);
            int n = write;
            int margin = _buffer.Length - _writePos;

            if (write > margin)
            {
                data.Slice(0, margin).CopyTo(_buffer.AsSpan(_writePos));
                _writePos = 0;
                n -= margin;
                _sameWrap = false;
            }
            data.Slice(write - n, n).CopyTo(_buffer.AsSpan(_writePos));
            _writePos += n;
            return write;

        }

        public int Read(byte[] data, int offset, int count)
        {
            int readCount = GetBufferReadRegions(count, out int dataIndex1, out int dataLen1, out int dataIndex2, out int dataLen2);
            if (dataLen2 > 0)
            {
                Buffer.BlockCopy(_buffer, dataIndex1, data, offset, dataLen1);
                Buffer.BlockCopy(_buffer, dataIndex2, data, offset + dataLen1, dataLen2);
            }
            else
            {
                // TODO(theomonnom): Don't always copy in this case?
                Buffer.BlockCopy(_buffer, dataIndex1, data, offset, dataLen1);
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

            return readable;
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
    }
}
