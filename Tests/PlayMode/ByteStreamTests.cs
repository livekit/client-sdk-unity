using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;

namespace LiveKit.PlayModeTests
{
    public class ByteStreamTests
    {
        [UnityTest, Category("E2E")]
        public IEnumerator StreamBytes_WriteAndClose_ReceivedViaReadAll()
        {
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "receiver";

            using var context = new TestRoomContext(new[] { sender, receiver });

            byte[] receivedBytes = null;
            var receivedExpectation = new Expectation(timeoutSeconds: 10f);

            context.Rooms[1].RegisterByteStreamHandler("byte-topic", (reader, identity) =>
            {
                var readAll = reader.ReadAll();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (!readAll.IsDone)
                        await System.Threading.Tasks.Task.Delay(10);

                    if (!readAll.IsError)
                    {
                        receivedBytes = readAll.Bytes;
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

            var streamInstruction = context.Rooms[0].LocalParticipant.StreamBytes("byte-topic");
            yield return streamInstruction;
            Assert.IsFalse(streamInstruction.IsError, "StreamBytes open failed");

            var writer = streamInstruction.Writer;
            var sentBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };

            var write = writer.Write(sentBytes);
            yield return write;
            Assert.IsFalse(write.IsError, "Write failed");

            var close = writer.Close("");
            yield return close;
            Assert.IsFalse(close.IsError, "Close failed");

            yield return receivedExpectation.Wait();
            Assert.IsNull(receivedExpectation.Error, receivedExpectation.Error);
            Assert.AreEqual(sentBytes, receivedBytes);
        }
    }
}
