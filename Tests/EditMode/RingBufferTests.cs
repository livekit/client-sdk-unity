using System;
using NUnit.Framework;
using LiveKit.Internal;

namespace LiveKit.EditModeTests
{
    public class RingBufferTests
    {
        [Test]
        public void Capacity_MatchesConstructorArgument()
        {
            using var rb = new RingBuffer(1024);
            Assert.AreEqual(1024, rb.Capacity);
        }

        [Test]
        public void Write_ReturnsNumberOfBytesWritten()
        {
            using var rb = new RingBuffer(256);
            var data = new byte[100];
            int written = rb.Write(data);
            Assert.AreEqual(100, written);
        }

        [Test]
        public void Write_WhenFull_ReturnsZero()
        {
            using var rb = new RingBuffer(64);
            rb.Write(new byte[64]);
            int written = rb.Write(new byte[1]);
            Assert.AreEqual(0, written);
        }

        [Test]
        public void Write_WhenPartiallyFull_WritesOnlyAvailable()
        {
            using var rb = new RingBuffer(64);
            rb.Write(new byte[50]);
            int written = rb.Write(new byte[30]);
            Assert.AreEqual(14, written);
        }

        [Test]
        public void Read_ReturnsNumberOfBytesRead()
        {
            using var rb = new RingBuffer(256);
            rb.Write(new byte[100]);
            var output = new byte[100];
            int read = rb.Read(output);
            Assert.AreEqual(100, read);
        }

        [Test]
        public void Read_WhenEmpty_ReturnsZero()
        {
            using var rb = new RingBuffer(64);
            var output = new byte[10];
            int read = rb.Read(output);
            Assert.AreEqual(0, read);
        }

        [Test]
        public void WriteAndRead_DataMatchesInput()
        {
            using var rb = new RingBuffer(256);
            var input = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
            rb.Write(input);

            var output = new byte[8];
            rb.Read(output);
            Assert.AreEqual(input, output);
        }

        [Test]
        public void AvailableRead_AfterWrite_MatchesWrittenLength()
        {
            using var rb = new RingBuffer(256);
            rb.Write(new byte[100]);
            Assert.AreEqual(100, rb.AvailableRead());
        }

        [Test]
        public void AvailableWrite_AfterWrite_ReducedByWrittenLength()
        {
            using var rb = new RingBuffer(256);
            rb.Write(new byte[100]);
            Assert.AreEqual(156, rb.AvailableWrite());
        }

        [Test]
        public void WriteAndRead_WithWraparound_DataIntegrity()
        {
            using var rb = new RingBuffer(64);

            // Fill most of the buffer and read it back to advance positions near the end
            var filler = new byte[50];
            for (int i = 0; i < 50; i++) filler[i] = (byte)i;
            rb.Write(filler);
            rb.Read(new byte[50]);

            // Now write data that wraps around the end of the buffer
            var input = new byte[40];
            for (int i = 0; i < 40; i++) input[i] = (byte)(0xA0 + i);
            int written = rb.Write(input);
            Assert.AreEqual(40, written);

            var output = new byte[40];
            int read = rb.Read(output);
            Assert.AreEqual(40, read);
            Assert.AreEqual(input, output);
        }

        [Test]
        public void AvailableReadInPercent_ReturnsCorrectRatio()
        {
            using var rb = new RingBuffer(100);
            rb.Write(new byte[50]);
            Assert.AreEqual(0.5f, rb.AvailableReadInPercent(), 0.01f);
        }

        [Test]
        public void AvailableWriteInPercent_ReturnsCorrectRatio()
        {
            using var rb = new RingBuffer(100);
            rb.Write(new byte[75]);
            Assert.AreEqual(0.25f, rb.AvailableWriteInPercent(), 0.01f);
        }

        [Test]
        public void Clear_ResetsState()
        {
            using var rb = new RingBuffer(256);
            rb.Write(new byte[100]);
            Assert.AreEqual(100, rb.AvailableRead());

            rb.Clear();
            Assert.AreEqual(0, rb.AvailableRead());
            Assert.AreEqual(256, rb.AvailableWrite());
        }

        [Test]
        public void SkipRead_AdvancesReadPosition()
        {
            using var rb = new RingBuffer(256);
            var input = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
            rb.Write(input);

            int skipped = rb.SkipRead(3);
            Assert.AreEqual(3, skipped);
            Assert.AreEqual(2, rb.AvailableRead());

            var output = new byte[2];
            rb.Read(output);
            Assert.AreEqual(new byte[] { 0x04, 0x05 }, output);
        }

        [Test]
        public void SkipRead_BeyondAvailable_ClampedToAvailable()
        {
            using var rb = new RingBuffer(64);
            rb.Write(new byte[10]);
            int skipped = rb.SkipRead(20);
            Assert.AreEqual(10, skipped);
            Assert.AreEqual(0, rb.AvailableRead());
        }

        [Test]
        public void MultipleWriteReadCycles_MaintainIntegrity()
        {
            using var rb = new RingBuffer(32);

            for (int cycle = 0; cycle < 10; cycle++)
            {
                var input = new byte[20];
                for (int i = 0; i < 20; i++) input[i] = (byte)(cycle * 20 + i);

                int written = rb.Write(input);
                Assert.AreEqual(20, written, $"Cycle {cycle}: write count mismatch");

                var output = new byte[20];
                int read = rb.Read(output);
                Assert.AreEqual(20, read, $"Cycle {cycle}: read count mismatch");
                Assert.AreEqual(input, output, $"Cycle {cycle}: data mismatch");
            }
        }
    }
}
