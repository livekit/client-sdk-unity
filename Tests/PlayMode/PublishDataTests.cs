using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;

namespace LiveKit.PlayModeTests
{
    // Probes the server-side size limit on LocalParticipant.PublishData. The C# API is
    // fire-and-forget (no callback), so the only observable signal is whether the
    // subscriber's DataReceived event fires within a timeout. The SFU data path may
    // not be ready immediately after connect, so we retry publishing on an interval.
    public class PublishDataTests
    {
        [UnityTest, Category("E2E")]
        public IEnumerator Small_1KiB_Arrives()
        {
            yield return RunSizeProbe(1024, shouldArrive: true);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator At_15KiB_Arrives()
        {
            yield return RunSizeProbe(15 * 1024, shouldArrive: true);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Above_64KiB_DoesNotArrive()
        {
            yield return RunSizeProbe(65 * 1024, shouldArrive: false);
        }

        private static IEnumerator RunSizeProbe(int payloadBytes, bool shouldArrive)
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "subscriber";

            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            var payload = new byte[payloadBytes];
            for (int i = 0; i < payloadBytes; i++) payload[i] = (byte)(i & 0xFF);

            byte[] received = null;
            subscriberRoom.DataReceived += (data, participant, kind, topic) =>
            {
                if (received == null) received = data;
            };

            float timeout = shouldArrive ? 5f : 3f;
            float start = Time.realtimeSinceStartup;
            float lastPublish = -1f;
            const float interval = 0.2f;
            while (received == null && Time.realtimeSinceStartup - start < timeout)
            {
                if (Time.realtimeSinceStartup - lastPublish >= interval)
                {
                    publisherRoom.LocalParticipant.PublishData(payload);
                    lastPublish = Time.realtimeSinceStartup;
                }
                yield return null;
            }

            if (shouldArrive)
            {
                Assert.IsNotNull(received,
                    $"Expected {payloadBytes}-byte payload to arrive within {timeout}s");
                Assert.AreEqual(payloadBytes, received.Length, "Received payload length mismatch");
                CollectionAssert.AreEqual(payload, received, "Received payload contents mismatch");
            }
            else
            {
                Assert.IsNull(received,
                    $"Expected {payloadBytes}-byte payload to be dropped, but it arrived ({received?.Length} bytes)");
            }
        }
    }
}
