using System.Collections;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// Disconnect() while Connect() is still pending has no room handle to close
    /// yet. It must be carried out when the connect completes, not dropped.
    /// </summary>
    public class DisconnectDuringConnectTests
    {
        const float TimeoutSeconds = 10f;

        [UnityTest, Category("E2E")]
        public IEnumerator Disconnect_WhilePending_TearsDownRoomWithoutConnected()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Identity = "disconnect-during-connect";
            using var context = new TestRoomContext(options);
            var room = context.Rooms[0];

            int connected = 0, disconnected = 0;
            DisconnectReason? reason = null;
            room.Connected += _ => connected++;
            room.Disconnected += _ => disconnected++;
            room.DisconnectedWithReason += (_, r) => reason = r;

            // Runs ConnectRoom up to its first yield: Connect() has been sent, its
            // callback has not arrived.
            var connect = context.ConnectRoom(0);
            connect.MoveNext();
            room.Disconnect();

            // Wait for the pending connect itself, then let the helper finish.
            yield return connect.Current;
            while (connect.MoveNext())
                yield return connect.Current;

            var done = new Expectation(() => disconnected == 1, TimeoutSeconds);
            yield return done.Wait();
            Assert.IsNull(done.Error, "Room was not disconnected after the pending connect completed.");

            Assert.IsNotNull(context.ConnectionError, "The pending connect reported success for a room disconnected mid-connect.");
            Assert.AreEqual(0, connected, "Connected must not be raised for a room disconnected mid-connect.");
            Assert.AreEqual(DisconnectReason.ClientInitiated, reason);
            Assert.IsFalse(room.IsConnected);
            Assert.IsNull(room.RoomHandle, "Room handle was not released.");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Room_CanReconnect_AfterDisconnectDuringConnect()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Identity = "disconnect-during-connect-reconnect";
            using var context = new TestRoomContext(options);
            var room = context.Rooms[0];

            var first = context.ConnectRoom(0);
            first.MoveNext();
            room.Disconnect();
            // Wait for the pending connect itself, then let the helper finish.
            yield return first.Current;
            while (first.MoveNext())
                yield return first.Current;
            Assert.IsNotNull(context.ConnectionError, "The pending connect reported success for a room disconnected mid-connect.");
            Assert.IsNull(room.RoomHandle);

            yield return context.ConnectRoom(0);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);
            Assert.IsTrue(room.IsConnected);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Connect_WhilePending_Throws()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Identity = "connect-while-pending";
            using var context = new TestRoomContext(options);
            var room = context.Rooms[0];

            var connect = context.ConnectRoom(0);
            connect.MoveNext();

            Assert.Throws<System.InvalidOperationException>(
                () => room.Connect("ws://localhost:7880", "token", new RoomOptions()));

            // Wait for the pending connect itself, then let the helper finish.
            yield return connect.Current;
            while (connect.MoveNext())
                yield return connect.Current;
            Assert.IsNull(context.ConnectionError, context.ConnectionError);
            Assert.IsTrue(room.IsConnected);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Connect_WhenAlreadyConnected_ThrowsAndKeepsRoom()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Identity = "connect-while-connected";
            using var context = new TestRoomContext(options);
            var room = context.Rooms[0];

            yield return context.ConnectRoom(0);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);
            var handle = room.RoomHandle;

            Assert.Throws<System.InvalidOperationException>(
                () => room.Connect("ws://localhost:7880", "token", new RoomOptions()));

            Assert.IsTrue(room.IsConnected);
            Assert.AreSame(handle, room.RoomHandle, "The live room handle was replaced.");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator E2EEManager_ReceivesRoomHandle()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Identity = "e2ee-handle";
            options.E2EE = new E2EEOptions
            {
                KeyProviderOptions = new KeyProviderOptions { SharedKey = new byte[] { 1, 2, 3, 4 } }
            };
            using var context = new TestRoomContext(options);
            var room = context.Rooms[0];

            yield return context.ConnectRoom(0);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            Assert.IsNotNull(room.E2EEManager);
            Assert.IsNotNull(room.E2EEManager.RoomHandle, "E2EEManager was created before the room handle existed.");
            Assert.AreSame(room.RoomHandle, room.E2EEManager.RoomHandle);
            Assert.AreSame(room.RoomHandle, room.E2EEManager.KeyProvider.RoomHandle);
        }
    }
}
