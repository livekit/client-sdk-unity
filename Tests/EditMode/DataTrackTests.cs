using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class DataTrackTests
    {
        [Test]
        public void DataTrackFrame_WithUserTimestampNow_SetsTimestamp()
        {
            var frame = new DataTrackFrame(new byte[] { 0x01, 0x02 });
            Assert.IsNull(frame.UserTimestamp);

            var stamped = frame.WithUserTimestampNow();
            Assert.IsNotNull(stamped.UserTimestamp);
            Assert.AreEqual(frame.Payload, stamped.Payload);
        }

        [Test]
        public void DataTrackFrame_DurationSinceTimestamp_NullWithoutTimestamp()
        {
            var frame = new DataTrackFrame(new byte[] { 0x01 });
            Assert.IsNull(frame.DurationSinceTimestamp());
        }

        [Test]
        public void DataTrackFrame_DurationSinceTimestamp_ReturnsValue()
        {
            var frame = new DataTrackFrame(new byte[] { 0x01 }).WithUserTimestampNow();
            var duration = frame.DurationSinceTimestamp();
            Assert.IsNotNull(duration);
            Assert.GreaterOrEqual(duration.Value, 0.0);
        }

        [Test]
        public void DataTrackFrame_WithUserTimestampNow_DoesNotMutateOriginal()
        {
            var original = new DataTrackFrame(new byte[] { 0x01 });
            Assert.IsNull(original.UserTimestamp);

            var stamped = original.WithUserTimestampNow();

            Assert.IsNull(original.UserTimestamp);
            Assert.IsNotNull(stamped.UserTimestamp);
            Assert.AreNotSame(original, stamped);
        }

        [Test]
        public void DataTrackFrame_WithUserTimestampNow_OverwritesExistingTimestamp()
        {
            var original = new DataTrackFrame(new byte[] { 0x01 }, userTimestamp: 42UL);
            Assert.AreEqual(42UL, original.UserTimestamp);

            var restamped = original.WithUserTimestampNow();

            Assert.IsNotNull(restamped.UserTimestamp);
            Assert.AreNotEqual(42UL, restamped.UserTimestamp);
        }

        [Test]
        public void DataTrackFrame_Constructor_WithExplicitTimestamp_SetsValue()
        {
            var frame = new DataTrackFrame(new byte[] { 0x01, 0x02 }, userTimestamp: 1234567UL);
            Assert.AreEqual(1234567UL, frame.UserTimestamp);
            Assert.AreEqual(new byte[] { 0x01, 0x02 }, frame.Payload);
        }
    }
}