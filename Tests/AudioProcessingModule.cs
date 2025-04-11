using System;
using NUnit.Framework;
using System.Runtime.InteropServices;

namespace LiveKit.Tests
{
    public class AudioProcessingModuleTest
    {
        [Test]
        public void TestAudioProcessing()
        {
            var apm = new AudioProcessingModule(true, true, true, true);
            apm.SetStreamDelayMs(100);
            apm.ProcessStream(CreateTestFrame());
            apm.ProcessReverseStream(CreateTestFrame());

            Assert.Throws<Exception>(() => apm.ProcessStream(CreateInvalidFrame()));
            Assert.Throws<Exception>(() => apm.ProcessReverseStream(CreateInvalidFrame()));
        }

        private AudioFrame CreateTestFrame()
        {
            const int SampleRate = 48000;
            const int NumChannels = 1;
            const int FramesPerChunk = SampleRate / 100;

            var frame = new AudioFrame(SampleRate, NumChannels, FramesPerChunk);

            var data = new short[frame.SamplesPerChannel * frame.NumChannels];
            for (int i = 0; i < data.Length; i++)
            {
                // Generate a 440Hz sine wave
                data[i] = (short)(short.MaxValue * Math.Sin(2 * Math.PI * 440 * i / frame.SampleRate));
            }
            Marshal.Copy(data, 0, frame.Data, data.Length);
            return frame;
        }

        private AudioFrame CreateInvalidFrame()
        {
            return new AudioFrame(100, 1, 1);
        }
    }
}