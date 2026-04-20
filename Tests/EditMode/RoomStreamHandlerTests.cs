using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class RoomStreamHandlerTests
    {
        [Test]
        public void RegisterTextStreamHandler_DuplicateTopic_ThrowsStreamError()
        {
            var room = new Room();
            room.RegisterTextStreamHandler("text-topic", (reader, identity) => { });

            var ex = Assert.Throws<StreamError>(
                () => room.RegisterTextStreamHandler("text-topic", (reader, identity) => { }));
            StringAssert.Contains("text-topic", ex.Message);
        }

        [Test]
        public void RegisterByteStreamHandler_DuplicateTopic_ThrowsStreamError()
        {
            var room = new Room();
            room.RegisterByteStreamHandler("byte-topic", (reader, identity) => { });

            var ex = Assert.Throws<StreamError>(
                () => room.RegisterByteStreamHandler("byte-topic", (reader, identity) => { }));
            StringAssert.Contains("byte-topic", ex.Message);
        }

        [Test]
        public void UnregisterTextStreamHandler_AllowsReregistrationOfSameTopic()
        {
            var room = new Room();
            room.RegisterTextStreamHandler("text-topic", (reader, identity) => { });
            room.UnregisterTextStreamHandler("text-topic");

            Assert.DoesNotThrow(
                () => room.RegisterTextStreamHandler("text-topic", (reader, identity) => { }));
        }

        [Test]
        public void UnregisterByteStreamHandler_AllowsReregistrationOfSameTopic()
        {
            var room = new Room();
            room.RegisterByteStreamHandler("byte-topic", (reader, identity) => { });
            room.UnregisterByteStreamHandler("byte-topic");

            Assert.DoesNotThrow(
                () => room.RegisterByteStreamHandler("byte-topic", (reader, identity) => { }));
        }
    }
}
