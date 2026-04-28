using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;

namespace LiveKit.PlayModeTests
{
    public class RpcTests
    {
        // Known Issue: CLT-2778: fails when not run before error tests. Passes in isolation or with Order(0),
        // but fails with "custom error Expected: False But was: True" at the RPC invocation
        // assertion when other tests run before it — suggests leaked/shared state between tests.
        [UnityTest, Category("E2E"), Order(0)]
        public IEnumerator RegisterRpcMethod_AndPerformRpc_ReturnsResponse()
        {
            LogAssert.ignoreFailingMessages = true;

            var caller = TestRoomContext.ConnectionOptions.Default;
            caller.Identity = "rpc-caller-1";
            var responder = TestRoomContext.ConnectionOptions.Default;
            responder.Identity = "rpc-responder-1";

            using var context = new TestRoomContext(new[] { caller, responder });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            context.Rooms[1].LocalParticipant.RegisterRpcMethod("echo", async (data) =>
            {
                return $"echo:{data.Payload}";
            });

            var rpcInstruction = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "echo",
                Payload = "hello"
            });
            yield return rpcInstruction;

            Assert.IsFalse(rpcInstruction.IsError, rpcInstruction.Error?.Message);
            Assert.AreEqual("echo:hello", rpcInstruction.Payload);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PerformRpc_HandlerThrowsRpcError_PropagatesError()
        {
            LogAssert.ignoreFailingMessages = true;

            var caller = TestRoomContext.ConnectionOptions.Default;
            caller.Identity = "rpc-caller-2";
            var responder = TestRoomContext.ConnectionOptions.Default;
            responder.Identity = "rpc-responder-2";

            using var context = new TestRoomContext(new[] { caller, responder });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            context.Rooms[1].LocalParticipant.RegisterRpcMethod("throw-rpc", async (data) =>
            {
                throw new RpcError(42, "custom error", "custom data");
            });

            var rpcInstruction = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "throw-rpc",
                Payload = "trigger-error"
            });
            yield return rpcInstruction;

            Assert.IsTrue(rpcInstruction.IsError);
            Assert.AreEqual((uint)42, rpcInstruction.Error.Code);
            Assert.AreEqual("custom data", rpcInstruction.Error.RpcData);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PerformRpc_HandlerThrowsGenericException_ReturnsApplicationError()
        {
            LogAssert.ignoreFailingMessages = true;

            var caller = TestRoomContext.ConnectionOptions.Default;
            caller.Identity = "rpc-caller-3";
            var responder = TestRoomContext.ConnectionOptions.Default;
            responder.Identity = "rpc-responder-3";

            using var context = new TestRoomContext(new[] { caller, responder });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            context.Rooms[1].LocalParticipant.RegisterRpcMethod("throw-generic", async (data) =>
            {
                throw new InvalidOperationException("something went wrong");
            });

            var rpcInstruction = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "throw-generic",
                Payload = "trigger-generic-error"
            });
            yield return rpcInstruction;

            Assert.IsTrue(rpcInstruction.IsError);
            Assert.AreEqual((uint)RpcError.ErrorCode.APPLICATION_ERROR, rpcInstruction.Error.Code);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PerformRpc_UnregisteredMethod_ReturnsUnsupportedMethod()
        {
            LogAssert.ignoreFailingMessages = true;

            var caller = TestRoomContext.ConnectionOptions.Default;
            caller.Identity = "rpc-caller-4";
            var responder = TestRoomContext.ConnectionOptions.Default;
            responder.Identity = "rpc-responder-4";

            using var context = new TestRoomContext(new[] { caller, responder });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var rpcInstruction = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "nonexistent-method",
                Payload = ""
            });
            yield return rpcInstruction;

            Assert.IsTrue(rpcInstruction.IsError);
            Assert.AreEqual((uint)RpcError.ErrorCode.UNSUPPORTED_METHOD, rpcInstruction.Error.Code);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator UnregisterRpcMethod_ThenPerformRpc_ReturnsUnsupportedMethod()
        {
            LogAssert.ignoreFailingMessages = true;

            var caller = TestRoomContext.ConnectionOptions.Default;
            caller.Identity = "rpc-caller-5";
            var responder = TestRoomContext.ConnectionOptions.Default;
            responder.Identity = "rpc-responder-5";

            using var context = new TestRoomContext(new[] { caller, responder });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            context.Rooms[1].LocalParticipant.RegisterRpcMethod("unreg-test", async (data) =>
            {
                return "should not reach here";
            });
            context.Rooms[1].LocalParticipant.UnregisterRpcMethod("unreg-test");

            var rpcInstruction = context.Rooms[0].LocalParticipant.PerformRpc(new PerformRpcParams
            {
                DestinationIdentity = responder.Identity,
                Method = "unreg-test",
                Payload = ""
            });
            yield return rpcInstruction;

            Assert.IsTrue(rpcInstruction.IsError);
            Assert.AreEqual((uint)RpcError.ErrorCode.UNSUPPORTED_METHOD, rpcInstruction.Error.Code);
        }

        // Known Issue: CLT-2778: fails when not run before error tests. Passes in isolation or with Order(0),
        // but fails with "custom error Expected: False But was: True" at the RPC invocation
        // assertion when other tests run before it — suggests leaked/shared state between tests.
        [UnityTest, Category("E2E"), Order(1)]
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
