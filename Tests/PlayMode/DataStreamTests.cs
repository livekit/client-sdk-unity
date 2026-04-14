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

        [Test]
        public void UnregisterTextHandler_ThenRegisterSameTopic_Succeeds()
        {
            var room = new Room();
            room.RegisterTextStreamHandler(TOPIC, (reader, participant) => { });
            room.UnregisterTextStreamHandler(TOPIC);
            Assert.DoesNotThrow(() => room.RegisterTextStreamHandler(TOPIC, (reader, participant) => { }));
        }

        [Test]
        public void UnregisterByteHandler_ThenRegisterSameTopic_Succeeds()
        {
            var room = new Room();
            room.RegisterByteStreamHandler(TOPIC, (reader, participant) => { });
            room.UnregisterByteStreamHandler(TOPIC);
            Assert.DoesNotThrow(() => room.RegisterByteStreamHandler(TOPIC, (reader, participant) => { }));
        }

        [Test]
        public void RegisterTextAndByteHandler_SameTopic_BothSucceed()
        {
            var room = new Room();
            Assert.DoesNotThrow(() =>
            {
                room.RegisterTextStreamHandler(TOPIC, (reader, participant) => { });
                room.RegisterByteStreamHandler(TOPIC, (reader, participant) => { });
            });
        }
    }
}
