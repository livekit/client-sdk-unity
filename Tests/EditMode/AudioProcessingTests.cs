using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    /// <summary>
    /// Pure-managed tests for the Unity-audio processing helpers: the 10 ms re-chunking, sample
    /// conversion, rate rules and the delay hint. No FFI, no audio device, so they always run.
    /// </summary>
    public class AudioProcessingTests
    {
        [Test]
        public void PcmRingBuffer_DrainsInWriteOrder()
        {
            var ring = new PcmRingBuffer(8);
            ring.Write(new short[] { 1, 2, 3, 4, 5 }, 0, 5);

            var dest = new short[3];
            Assert.IsTrue(ring.TryDrain(dest, 3));
            Assert.AreEqual(new short[] { 1, 2, 3 }, dest);
            Assert.AreEqual(2, ring.Available);

            var rest = new short[2];
            Assert.IsTrue(ring.TryDrain(rest, 2));
            Assert.AreEqual(new short[] { 4, 5 }, rest);
            Assert.AreEqual(0, ring.Available);
            Assert.AreEqual(0, ring.OverflowSamples);
        }

        [Test]
        public void PcmRingBuffer_TryDrain_WithoutEnoughSamples_ConsumesNothing()
        {
            var ring = new PcmRingBuffer(8);
            ring.Write(new short[] { 1, 2 }, 0, 2);

            Assert.IsFalse(ring.TryDrain(new short[3], 3));
            Assert.AreEqual(2, ring.Available);
        }

        [Test]
        public void PcmRingBuffer_WhenFull_DropsOldest()
        {
            var ring = new PcmRingBuffer(4);
            ring.Write(new short[] { 1, 2, 3 }, 0, 3);
            ring.Write(new short[] { 4, 5 }, 0, 2);

            Assert.AreEqual(1, ring.OverflowSamples);
            var dest = new short[4];
            Assert.IsTrue(ring.TryDrain(dest, 4));
            Assert.AreEqual(new short[] { 2, 3, 4, 5 }, dest);
        }

        [Test]
        public void PcmRingBuffer_WriteLargerThanCapacity_KeepsNewest()
        {
            var ring = new PcmRingBuffer(4);
            ring.Write(new short[] { 1, 2 }, 0, 2);
            ring.Write(new short[] { 3, 4, 5, 6, 7 }, 0, 5);

            Assert.AreEqual(3, ring.OverflowSamples);
            var dest = new short[4];
            Assert.IsTrue(ring.TryDrain(dest, 4));
            Assert.AreEqual(new short[] { 4, 5, 6, 7 }, dest);
        }

        [Test]
        public void PcmRingBuffer_WrapsAround()
        {
            var ring = new PcmRingBuffer(4);
            ring.Write(new short[] { 1, 2, 3 }, 0, 3);
            Assert.IsTrue(ring.TryDrain(new short[2], 2));
            ring.Write(new short[] { 4, 5, 6 }, 0, 3);

            var dest = new short[4];
            Assert.IsTrue(ring.TryDrain(dest, 4));
            Assert.AreEqual(new short[] { 3, 4, 5, 6 }, dest);
            Assert.AreEqual(0, ring.OverflowSamples);
        }

        [Test]
        public void PcmRingBuffer_Clear_Empties()
        {
            var ring = new PcmRingBuffer(4);
            ring.Write(new short[] { 1, 2, 3 }, 0, 3);
            ring.Clear();

            Assert.AreEqual(0, ring.Available);
            Assert.IsFalse(ring.TryDrain(new short[1], 1));
        }

        [Test]
        public void PcmConvert_FloatToS16_ClampsAndRounds()
        {
            Assert.AreEqual(0, PcmConvert.FloatToS16(0f));
            Assert.AreEqual(16384, PcmConvert.FloatToS16(0.5f));
            Assert.AreEqual(-16384, PcmConvert.FloatToS16(-0.5f));
            Assert.AreEqual(short.MaxValue, PcmConvert.FloatToS16(1f));
            Assert.AreEqual(short.MinValue, PcmConvert.FloatToS16(-1f));
            Assert.AreEqual(short.MaxValue, PcmConvert.FloatToS16(3f));
            Assert.AreEqual(short.MinValue, PcmConvert.FloatToS16(-3f));
        }

        [Test]
        public void AudioProcessingModule_SupportedApiRates_NeedWholeSampleChunks()
        {
            // The Rust side asserts on a frame that is not a whole multiple of 10 ms, so a rate whose
            // 10 ms chunk is fractional must be refused up front.
            Assert.IsTrue(AudioProcessingModule.IsSupportedApiRate(48000));
            Assert.IsTrue(AudioProcessingModule.IsSupportedApiRate(44100));
            Assert.IsTrue(AudioProcessingModule.IsSupportedApiRate(24000));
            Assert.IsTrue(AudioProcessingModule.IsSupportedApiRate(16000));
            Assert.IsFalse(AudioProcessingModule.IsSupportedApiRate(22050));
            Assert.IsFalse(AudioProcessingModule.IsSupportedApiRate(11025));
            Assert.IsFalse(AudioProcessingModule.IsSupportedApiRate(0));
            Assert.IsFalse(AudioProcessingModule.IsSupportedApiRate(-48000));
        }

        [Test]
        public void AudioProcessingModule_FrameSizeFor_IsTenMilliseconds()
        {
            Assert.AreEqual(480, AudioProcessingModule.FrameSizeFor(48000));
            Assert.AreEqual(441, AudioProcessingModule.FrameSizeFor(44100));
            Assert.AreEqual(240, AudioProcessingModule.FrameSizeFor(24000));
            Assert.IsTrue(AudioProcessingModule.IsNativeSampleRate(48000));
            Assert.IsFalse(AudioProcessingModule.IsNativeSampleRate(44100));
        }

        [Test]
        public void AudioProcessingOptions_Default_EnablesHighPassFilter_AndReportsProcessing()
        {
            Assert.IsTrue(AudioProcessingOptions.Default.HighPassFilter);
            Assert.IsTrue(AudioProcessingOptions.Default.AnyProcessingEnabled);

            // An all-false struct means "no processing"; PreferHardware alone is not a stage.
            Assert.IsFalse(default(AudioProcessingOptions).AnyProcessingEnabled);
            Assert.IsFalse(new AudioProcessingOptions { PreferHardware = true }.AnyProcessingEnabled);
            Assert.IsTrue(new AudioProcessingOptions { HighPassFilter = true }.AnyProcessingEnabled);
        }

        [Test]
        public void DelayHint_SumsQueueDeviceAndMicrophoneTerms()
        {
            // 1024 frames at 48 kHz = 21.33 ms per block; two queued blocks + 30 ms device + 50 ms
            // microphone read-behind = 122.67 ms.
            Assert.AreEqual(123, AudioProcessingDelayHint.EstimateMs(1024, 48000, 30));
            Assert.AreEqual(AudioProcessingDelayHint.MicrophoneReadBehindMs, AudioProcessingDelayHint.EstimateMs(0, 0, 0));
        }

        [Test]
        public void DelayHint_ClampsToRange()
        {
            Assert.AreEqual(AudioProcessingDelayHint.MaxDelayMs, AudioProcessingDelayHint.EstimateMs(48000, 48000, 1000));
            Assert.AreEqual(AudioProcessingDelayHint.MinDelayMs, AudioProcessingDelayHint.EstimateMs(0, 0, -1000));
        }
    }
}
