using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;

namespace LiveKit.PlayModeTests
{
    public class TextStreamTests
    {
        [UnityTest, Category("E2E")]
        public IEnumerator SendText_ReceivedByHandler_ContentMatches()
        {
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "receiver";

            using var context = new TestRoomContext(new[] { sender, receiver });

            string receivedText = null;
            string receivedIdentity = null;
            var receivedExpectation = new Expectation(timeoutSeconds: 10f);

            context.Rooms[1].RegisterTextStreamHandler("test-topic", (reader, identity) =>
            {
                receivedIdentity = identity;
                var readAll = reader.ReadAll();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (!readAll.IsDone)
                        await System.Threading.Tasks.Task.Delay(10);

                    if (!readAll.IsError)
                    {
                        receivedText = readAll.Text;
                        receivedExpectation.Fulfill();
                    }
                    else
                    {
                        receivedExpectation.Fail("ReadAll failed");
                    }
                });
            });

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var sendInstruction = context.Rooms[0].LocalParticipant.SendText("hello world", "test-topic");
            yield return sendInstruction;
            Assert.IsFalse(sendInstruction.IsError, "SendText failed");

            yield return receivedExpectation.Wait();
            Assert.IsNull(receivedExpectation.Error, receivedExpectation.Error);
            Assert.AreEqual("hello world", receivedText);
            Assert.AreEqual(sender.Identity, receivedIdentity);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator SendText_WithTopic_DispatchesToCorrectHandler()
        {
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "receiver";

            using var context = new TestRoomContext(new[] { sender, receiver });

            string topicAText = null;
            string topicBText = null;
            var topicAExpectation = new Expectation(timeoutSeconds: 10f);
            var topicBExpectation = new Expectation(timeoutSeconds: 10f);

            context.Rooms[1].RegisterTextStreamHandler("topic-a", (reader, identity) =>
            {
                var readAll = reader.ReadAll();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (!readAll.IsDone) await System.Threading.Tasks.Task.Delay(10);
                    if (!readAll.IsError)
                    {
                        topicAText = readAll.Text;
                        topicAExpectation.Fulfill();
                    }
                    else topicAExpectation.Fail("ReadAll failed for topic-a");
                });
            });

            context.Rooms[1].RegisterTextStreamHandler("topic-b", (reader, identity) =>
            {
                var readAll = reader.ReadAll();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (!readAll.IsDone) await System.Threading.Tasks.Task.Delay(10);
                    if (!readAll.IsError)
                    {
                        topicBText = readAll.Text;
                        topicBExpectation.Fulfill();
                    }
                    else topicBExpectation.Fail("ReadAll failed for topic-b");
                });
            });

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var sendA = context.Rooms[0].LocalParticipant.SendText("message-a", "topic-a");
            yield return sendA;
            Assert.IsFalse(sendA.IsError, "SendText to topic-a failed");

            var sendB = context.Rooms[0].LocalParticipant.SendText("message-b", "topic-b");
            yield return sendB;
            Assert.IsFalse(sendB.IsError, "SendText to topic-b failed");

            yield return topicAExpectation.Wait();
            Assert.IsNull(topicAExpectation.Error, topicAExpectation.Error);
            Assert.AreEqual("message-a", topicAText);

            yield return topicBExpectation.Wait();
            Assert.IsNull(topicBExpectation.Error, topicBExpectation.Error);
            Assert.AreEqual("message-b", topicBText);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator StreamText_WriteAndClose_ReceivedViaReadAll()
        {
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "receiver";

            using var context = new TestRoomContext(new[] { sender, receiver });

            string receivedText = null;
            var receivedExpectation = new Expectation(timeoutSeconds: 10f);

            context.Rooms[1].RegisterTextStreamHandler("stream-topic", (reader, identity) =>
            {
                var readAll = reader.ReadAll();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (!readAll.IsDone) await System.Threading.Tasks.Task.Delay(10);
                    if (!readAll.IsError)
                    {
                        receivedText = readAll.Text;
                        receivedExpectation.Fulfill();
                    }
                    else receivedExpectation.Fail("ReadAll failed");
                });
            });

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var streamInstruction = context.Rooms[0].LocalParticipant.StreamText("stream-topic");
            yield return streamInstruction;
            Assert.IsFalse(streamInstruction.IsError, "StreamText open failed");

            var writer = streamInstruction.Writer;

            var write1 = writer.Write("Hello ");
            yield return write1;
            Assert.IsFalse(write1.IsError, "Write chunk 1 failed");

            var write2 = writer.Write("World");
            yield return write2;
            Assert.IsFalse(write2.IsError, "Write chunk 2 failed");

            var close = writer.Close("");
            yield return close;
            Assert.IsFalse(close.IsError, "Close failed");

            yield return receivedExpectation.Wait();
            Assert.IsNull(receivedExpectation.Error, receivedExpectation.Error);
            Assert.AreEqual("Hello World", receivedText);
        }

        [UnityTest, Category("E2E"), Ignore("Known issue: CLT-2774")]
        public IEnumerator StreamText_CloseWithoutReason_Succeeds()
        {
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "receiver";

            using var context = new TestRoomContext(new[] { sender, receiver });

            string receivedText = null;
            var receivedExpectation = new Expectation(timeoutSeconds: 10f);

            context.Rooms[1].RegisterTextStreamHandler("close-topic", (reader, identity) =>
            {
                var readAll = reader.ReadAll();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (!readAll.IsDone) await System.Threading.Tasks.Task.Delay(10);
                    if (!readAll.IsError)
                    {
                        receivedText = readAll.Text;
                        receivedExpectation.Fulfill();
                    }
                    else receivedExpectation.Fail("ReadAll failed");
                });
            });

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var streamInstruction = context.Rooms[0].LocalParticipant.StreamText("close-topic");
            yield return streamInstruction;
            Assert.IsFalse(streamInstruction.IsError, "StreamText open failed");

            var writer = streamInstruction.Writer;

            var write = writer.Write("test");
            yield return write;
            Assert.IsFalse(write.IsError, "Write failed");

            // Call Close() with no arguments — this is the bug regression test.
            // Before fix: throws ArgumentNullException because default reason=null
            // hits ProtoPreconditions.CheckNotNull in the generated proto setter.
            var close = writer.Close();
            yield return close;
            Assert.IsFalse(close.IsError, "Close without reason failed");

            yield return receivedExpectation.Wait();
            Assert.IsNull(receivedExpectation.Error, receivedExpectation.Error);
            Assert.AreEqual("test", receivedText);
        }
    }
}
