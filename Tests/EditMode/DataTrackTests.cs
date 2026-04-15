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
    }
}