using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class RoomOptionsTests
    {
        [Test]
        public void RTCConfiguration_ToProto_DefaultInstance_DoesNotThrow()
        {
            var config = new RTCConfiguration();
            Assert.DoesNotThrow(() => config.ToProto());
        }

        [Test]
        public void IceServer_ToProto_WithAllFieldsSet_Succeeds()
        {
            var server = new IceServer
            {
                Urls = new[] { "stun:stun.example.com:3478" },
                Username = "user",
                Password = "pass"
            };

            var proto = server.ToProto();

            Assert.AreEqual("user", proto.Username);
            Assert.AreEqual("pass", proto.Password);
            Assert.AreEqual(1, proto.Urls.Count);
            Assert.AreEqual("stun:stun.example.com:3478", proto.Urls[0]);
        }

        [Test]
        public void IceServer_ToProto_WithNullUrls_DoesNotThrow()
        {
            var server = new IceServer { Username = "u", Password = "p" };
            Assert.DoesNotThrow(() => server.ToProto());
        }
    }
}
