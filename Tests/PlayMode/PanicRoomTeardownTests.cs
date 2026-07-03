using System.Collections;
using System.Text.RegularExpressions;
using LiveKit.Internal.FFI;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// An FfiEvent.Panic means the FFI layer's state is unrecoverable — its
    /// background tasks may have died, leaving rooms that silently stop
    /// receiving events (they would not even get a Disconnected event). The
    /// SDK must convert that into an observable disconnect on every live
    /// room instead of dropping the event.
    /// </summary>
    public class PanicRoomTeardownTests
    {
        const float DisconnectTimeoutSeconds = 10f;

        [UnityTest, Category("E2E")]
        public IEnumerator Panic_DisconnectsConnectedRoom()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Identity = "panic-teardown";
            using var context = new TestRoomContext(options);

            yield return context.ConnectRoom(0);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var room = context.Rooms[0];
            Room disconnectedRoom = null;
            DisconnectReason? reason = null;
            room.Disconnected += r => disconnectedRoom = r;
            room.DisconnectedWithReason += (r, dr) => reason = dr;

            LogAssert.Expect(LogType.Error, new Regex("FFI panic"));

            // Inject a synthetic panic through the same entry point the native
            // callback uses; it is posted to the main thread like real events.
            FfiClient.RouteFfiEvent(new FfiEvent
            {
                Panic = new Panic { Message = "synthetic panic (test)" }
            });

            var disconnected = new Expectation(
                predicate: () => disconnectedRoom != null,
                timeoutSeconds: DisconnectTimeoutSeconds);
            yield return disconnected.Wait();

            Assert.IsNull(disconnected.Error,
                "Room did not raise Disconnected after an FFI panic event.");
            Assert.AreSame(room, disconnectedRoom,
                "Disconnected fired for a different room instance.");
            Assert.AreEqual(DisconnectReason.UnknownReason, reason,
                "DisconnectedWithReason did not carry the expected reason.");
            Assert.AreEqual(DisconnectReason.UnknownReason, room.DisconnectReason,
                "Room.DisconnectReason was not set by the panic teardown.");
            Assert.IsFalse(room.IsConnected,
                "Room still reports IsConnected after the panic teardown.");
        }
    }
}
