using LiveKit.Audio;
using LiveKit.Internal;
using LiveKit.Rooms.Streaming.Audio;
using NUnit.Framework;

namespace LiveKit.Tests
{
    public class OwnedFrameTest
    {
        [Test]
        public void ResampleTest()
        {
            using AudioResampler audioResampler = AudioResampler.New();
            using AudioFrame frame = new AudioFrame(41000, 2, 441);
            
            using OwnedAudioFrame resampled = audioResampler.RemixAndResample(frame, 2, 48000 /*44100*/);

            Assert.AreEqual(44100, resampled.SampleRate);
            Assert.AreEqual(2, resampled.NumChannels);
        }
    }
}