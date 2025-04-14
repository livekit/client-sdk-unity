using System;
using NUnit.Framework;

namespace LiveKit.Tests
{
    public class AudioBufferTest
    {
        [Test]
        [TestCase(24000u, 1u, 10u)]
        [TestCase(48000u, 2u, 10u)]
        public void TestWriteAndRead(uint sampleRate, uint channels, uint durationMs)
        {
            var buffer = new AudioBuffer();

            Assert.IsNull(buffer.ReadDuration(durationMs), "Should not be able to read from empty buffer");

            var samples = TestUtils.GenerateSineWave(channels, sampleRate, durationMs);
            buffer.Write(samples, channels, sampleRate);

            Assert.IsNull(buffer.ReadDuration(durationMs * 2), "Should not be enough samples for this read");

            var frame = buffer.ReadDuration(durationMs);
            Assert.IsNotNull(frame);
            Assert.AreEqual(sampleRate, frame.SampleRate);
            Assert.AreEqual(channels, frame.NumChannels);
            Assert.AreEqual(samples.Length / channels, frame.SamplesPerChannel);

            Assert.IsNull(buffer.ReadDuration(durationMs), "Should not be able to read again");
        }
    }
}