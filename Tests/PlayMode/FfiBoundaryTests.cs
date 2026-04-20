using System.Collections;
using System.Threading.Tasks;
using LiveKit.PlayModeTests.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    public class FfiBoundaryTests
    {
        [UnityTest, Category("E2E")]
        public IEnumerator Disconnect_CalledTwice_DoesNotThrow()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var room = context.Rooms[0];
            Assert.DoesNotThrow(() => room.Disconnect(), "first Disconnect threw");
            Assert.DoesNotThrow(() => room.Disconnect(), "second Disconnect threw");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PerformRpc_NearLimitPayload_EchoesRoundTrip()
        {
            // 14 KiB is comfortably below the documented 15 KiB limit on both the
            // request payload and the response string. Using plain ASCII so byte-count
            // == char-count.
            const int payloadSize = 14 * 1024;
            var largePayload = new string('A', payloadSize);

            var caller = TestRoomContext.ConnectionOptions.Default;
            caller.Identity = "rpc-large-caller";
            var responder = TestRoomContext.ConnectionOptions.Default;
            responder.Identity = "rpc-large-responder";

            using var context = new TestRoomContext(new[] { caller, responder });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            context.Rooms[1].LocalParticipant.RegisterRpcMethod("echo", async (data) =>
            {
                await Task.Yield();
                return data.Payload;
            });

            var rpc = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "echo",
                Payload = largePayload,
                ResponseTimeout = 15f
            });
            yield return rpc;

            Assert.IsFalse(rpc.IsError, rpc.Error?.Message);
            Assert.AreEqual(payloadSize, rpc.Payload.Length);
            Assert.AreEqual(largePayload, rpc.Payload);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PerformRpc_ConcurrentInvocations_AllComplete()
        {
            // Three RPCs dispatched without yielding between them exercise the FFI
            // request pool and assert that each PerformRpcInstruction resolves with
            // its own distinct request_async_id and payload.
            var caller = TestRoomContext.ConnectionOptions.Default;
            caller.Identity = "rpc-concurrent-caller";
            var responder = TestRoomContext.ConnectionOptions.Default;
            responder.Identity = "rpc-concurrent-responder";

            using var context = new TestRoomContext(new[] { caller, responder });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            context.Rooms[1].LocalParticipant.RegisterRpcMethod("echo", async (data) =>
            {
                await Task.Yield();
                return $"echo:{data.Payload}";
            });

            var rpc1 = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "echo",
                Payload = "one"
            });
            var rpc2 = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "echo",
                Payload = "two"
            });
            var rpc3 = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "echo",
                Payload = "three"
            });

            yield return rpc1;
            yield return rpc2;
            yield return rpc3;

            Assert.IsFalse(rpc1.IsError, rpc1.Error?.Message);
            Assert.IsFalse(rpc2.IsError, rpc2.Error?.Message);
            Assert.IsFalse(rpc3.IsError, rpc3.Error?.Message);

            Assert.AreEqual("echo:one", rpc1.Payload);
            Assert.AreEqual("echo:two", rpc2.Payload);
            Assert.AreEqual("echo:three", rpc3.Payload);
        }
    }
}
