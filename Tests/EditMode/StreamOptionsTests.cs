using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class StreamOptionsTests
    {
        [Test]
        public void TextOptions_CompressUnset_LeavesProtoFieldUnset()
        {
            var proto = new StreamTextOptions { Topic = "my-topic" }.ToProto();

            Assert.IsFalse(proto.HasCompress);
        }

        [Test]
        public void TextOptions_CompressFalse_SetsProtoField()
        {
            var proto = new StreamTextOptions { Topic = "my-topic", Compress = false }.ToProto();

            Assert.IsTrue(proto.HasCompress);
            Assert.IsFalse(proto.Compress);
        }

        [Test]
        public void ByteOptions_CompressUnset_LeavesProtoFieldUnset()
        {
            var proto = new StreamByteOptions { Topic = "my-topic" }.ToProto();

            Assert.IsFalse(proto.HasCompress);
        }

        [Test]
        public void ByteOptions_CompressFalse_SetsProtoField()
        {
            var proto = new StreamByteOptions { Topic = "my-topic", Compress = false }.ToProto();

            Assert.IsTrue(proto.HasCompress);
            Assert.IsFalse(proto.Compress);
        }
    }
}
