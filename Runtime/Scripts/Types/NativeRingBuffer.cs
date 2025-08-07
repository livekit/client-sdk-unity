using System;
using RichTypes;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LiveKit.Types
{
    /// <summary>
    /// High performance zero allocate ring buffer
    /// </summary>
    public struct NativeRingBuffer<T> : IDisposable where T : unmanaged
    {
        private readonly IntPtr ptr;
        private readonly IntPtr readBufferPtr;
        private readonly IntPowerOf2 capacity;
        private int head;
        private int tail;

        public readonly bool IsEmpty => head == tail;
        public readonly bool IsFull => head - tail == capacity.value;
        public readonly int Count => head - tail;

        public NativeRingBuffer(IntPowerOf2 capacity)
        {
            this.capacity = capacity;
            head = 0;
            tail = 0;
            unsafe
            {
                int size = capacity.value * sizeof(T);
                int alignment = UnsafeUtility.AlignOf<T>();

                ptr = new IntPtr(UnsafeUtility.Malloc(size, alignment, Allocator.Persistent)!);
                readBufferPtr = new IntPtr(UnsafeUtility.Malloc(size, alignment, Allocator.Persistent)!);
            }
        }

        public void Dispose()
        {
            unsafe
            {
                if (ptr != IntPtr.Zero)
                {
                    UnsafeUtility.Free(ptr.ToPointer()!, Allocator.Persistent);
                }

                if (readBufferPtr != IntPtr.Zero)
                {
                    UnsafeUtility.Free(readBufferPtr.ToPointer()!, Allocator.Persistent);
                }
            }
        }

        /// <summary>
        /// Overwrites written values if span length exceeds free space
        /// </summary>
        public void Enqueue(ReadOnlySpan<T> span)
        {
            if (span.Length == 0)
                return;

            int free = capacity.value - Count;
            // If incoming span is bigger than free space, we'll overwrite
            int toOverwrite = Math.Max(0, span.Length - free);

            unsafe
            {
                T* buffer = (T*)ptr.ToPointer();

                int firstPart = Math.Min(span.Length, capacity.value - (head & (capacity.value - 1)));
                int secondPart = span.Length - firstPart;

                // Copy first contiguous part
                span.Slice(0, firstPart).CopyTo(
                    new Span<T>(
                        buffer + (head & (capacity.value - 1)),
                        firstPart
                    )
                );

                // Copy second wrapped part
                if (secondPart > 0)
                {
                    span.Slice(firstPart, secondPart).CopyTo(
                        new Span<T>(buffer, secondPart)
                    );
                }
            }

            head += span.Length;
            // Move tail forward if we overwrote existing data
            if (toOverwrite > 0) tail += toOverwrite;
        }

        /// <summary>
        /// Dequeues chunk of data
        /// </summary>
        /// <param name="length"> Required length to read</param>
        /// <param name="success"> Success if complete to read required length, If buffer doesn't have required length - returns false</param>
        /// <returns>In success case - temp filled span, otherwise - empty span, Span is valid until next read</returns>
        public Span<T> TryDequeue(int length, out bool success)
        {
            if (Count < length)
            {
                success = false;
                return Span<T>.Empty;
            }


            unsafe
            {
                T* buffer = (T*)ptr.ToPointer();
                T* readBuffer = (T*)readBufferPtr.ToPointer();

                // Compute how many elements we can copy in one go (before wrapping)
                int firstPart = Math.Min(length, capacity.value - (tail & (capacity.value - 1)));
                int secondPart = length - firstPart;

                // Copy first part
                new ReadOnlySpan<T>(buffer + (tail & (capacity.value - 1)), firstPart)
                    .CopyTo(new Span<T>(readBuffer, length));

                // Copy wrapped part if needed
                if (secondPart > 0)
                {
                    new ReadOnlySpan<T>(buffer, secondPart)
                        .CopyTo(new Span<T>(readBuffer + firstPart, secondPart));
                }

                tail += length;
                success = true;

                return new Span<T>(readBuffer, length);
            }
        }

    }

    public readonly struct IntPowerOf2
    {
        public readonly int value;

        private IntPowerOf2(int value)
        {
            this.value = value;
        }

        public static Result<IntPowerOf2> New(int value)
        {
            if (value <= 0 || (value & (value - 1)) != 0)
                return Result<IntPowerOf2>.ErrorResult("Capacity must be power of 2.");
            return Result<IntPowerOf2>.SuccessResult(new IntPowerOf2(value));
        }

        public static IntPowerOf2 NewOrNextPowerOf2(int value)
        {
            value = NextPowerOfTwo(value);
            return new IntPowerOf2(value);
        }

        private static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v;
        }
    }
}