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
    }
}
