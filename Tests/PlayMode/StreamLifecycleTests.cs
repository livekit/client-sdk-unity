using System.Collections;
using System.IO;
using LiveKit.PlayModeTests.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    public class StreamLifecycleTests
    {
        [UnityTest, Category("E2E")]
        public IEnumerator SendFile_RecipientReceivesBytes()
        {
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "file-sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "file-receiver";

            using var context = new TestRoomContext(new[] { sender, receiver });

            const string topic = "file-topic";
            var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
            var tempFile = Path.GetTempFileName();
            File.WriteAllBytes(tempFile, payload);

            byte[] received = null;
            var receivedExp = new Expectation(timeoutSeconds: 10f);

            context.Rooms[1].RegisterByteStreamHandler(topic, (reader, identity) =>
            {
                var readAll = reader.ReadAll();
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (!readAll.IsDone)
                        await System.Threading.Tasks.Task.Delay(10);

                    if (readAll.IsError)
                        receivedExp.Fail("reader.ReadAll errored");
                    else
                    {
                        received = readAll.Bytes;
                        receivedExp.Fulfill();
                    }
                });
            });

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            try
            {
                var sendInstruction = context.Rooms[0].LocalParticipant.SendFile(tempFile, topic);
                yield return sendInstruction;
                Assert.IsFalse(sendInstruction.IsError, "SendFile reported IsError");

                yield return receivedExp.Wait();
                Assert.IsNull(receivedExp.Error, receivedExp.Error);
                Assert.AreEqual(payload, received);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
