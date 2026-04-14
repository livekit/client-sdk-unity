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
    }
}
