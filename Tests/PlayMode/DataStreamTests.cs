using NUnit.Framework;

namespace LiveKit.PlayModeTests
{
    public class DataStreamTests
    {
        private const string TOPIC = "test-topic";

        [Test]
        public void RegisterTextHandler_FailsWithDuplicateTopic()
        {
            var room = new Room();
            room.RegisterTextStreamHandler(TOPIC, (reader, participant) => { });
            Assert.Throws<StreamError>(() => room.RegisterTextStreamHandler(TOPIC, (reader, participant) => { }));
        }

        [Test]
        public void RegisterByteHandler_FailsWithDuplicateTopic()
        {
            var room = new Room();
            room.RegisterByteStreamHandler(TOPIC, (reader, participant) => { });
            Assert.Throws<StreamError>(() => room.RegisterByteStreamHandler(TOPIC, (reader, participant) => { }));
        }
    }
}
