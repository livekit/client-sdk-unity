using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class RoomDisconnectTests
    {
        [Test]
        public void Disconnect_OnNeverConnectedRoom_DoesNotThrow()
        {
            // Room with no FFI handle: the early-return at Room.cs:186 must cover this
            // so client code can safely clean up without tracking connection state.
            var room = new Room();
            Assert.DoesNotThrow(() => room.Disconnect());
        }

        [Test]
        public void Disconnect_OnNeverConnectedRoom_CalledTwice_DoesNotThrow()
        {
            var room = new Room();
            room.Disconnect();
            Assert.DoesNotThrow(() => room.Disconnect());
        }
    }
}
