using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;

namespace LiveKit.PlayModeTests
{
    // Probes LocalParticipant.PublishData delivery and the result it returns.
    //
    // The *_Arrives / *_DoesNotArrive tests observe end-to-end delivery via the
    // subscriber's DataReceived event. The SFU data path may not be ready
    // immediately after connect, so we retry publishing on an interval.
    //
    // The Returns* tests instead inspect the PublishDataInstruction returned by
    // PublishData, which reports whether the Rust side accepted the packet.
    public class PublishDataTests
    {
        // The returned instruction completes without error for a payload within the
        // size limit. Runs against any FFI binary.
        [UnityTest, Category("E2E")]
        public IEnumerator ReturnsSuccess_ForSmallPayload()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "subscriber";

            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var instruction = context.Rooms[0].LocalParticipant.PublishData(new byte[1024]);
            yield return instruction;

            Assert.IsTrue(instruction.IsDone, "PublishData instruction did not complete");
            Assert.IsFalse(instruction.IsError, $"Unexpected publish error: {instruction.Error}");
            Assert.IsNull(instruction.Error);
        }

        // A payload over the negotiated maximum message size completes with an error.
        // Marked Explicit because the currently-shipped FFI plugins do not enforce the
        // limit — only the client-sdk-rust `datamessage_size` binary does. Run locally
        // against that binary with:
        //   Scripts~/run_unity.sh test -m PlayMode -f PublishDataTests.ReturnsError_ForOversizedPayload
        [UnityTest, Category("E2E")]
        public IEnumerator ReturnsError_ForOversizedPayload()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "subscriber";

            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            // With LK_VERBOSE defined, the native layer forwards the size-limit log to
            // Unity, which would otherwise fail the test. Ignore failing log messages
            // for the rest of the test (same approach as RoomTests).
            LogAssert.ignoreFailingMessages = true;

            // Establish the publisher data channel with an in-limit publish first. The
            // size check only engages once SCTP has negotiated the max message size
            // (which happens when the reliable data channel opens). Sending an oversized
            // reliable packet *before* that negotiation can wedge the publisher
            // transport (manifesting as a connection timeout), so we must warm up first.
            var warmup = context.Rooms[0].LocalParticipant.PublishData(new byte[1024]);
            yield return warmup;
            Assert.IsFalse(warmup.IsError, $"Warmup publish failed: {warmup.Error}");

            // Now probe with the oversized payload. Retry a few times in case the
            // negotiated max size lands slightly after the channel opens.
            var oversized = new byte[65 * 1024];
            var retryDelay = new WaitForSeconds(0.2f);
            PublishDataInstruction instruction = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                instruction = context.Rooms[0].LocalParticipant.PublishData(oversized);
                yield return instruction;
                if (instruction.IsError) break;
                yield return retryDelay;
            }

            Assert.IsNotNull(instruction, "No publish was attempted");
            Assert.IsTrue(instruction.IsDone, "PublishData instruction did not complete");
            Assert.IsTrue(instruction.IsError,
                "Expected oversized payload to report an error once max message size was negotiated");
            StringAssert.Contains("maximum message size", instruction.Error);
        }
    }
}
