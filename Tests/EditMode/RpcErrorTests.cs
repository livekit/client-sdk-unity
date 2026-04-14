using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class RpcErrorTests
    {
        [Test]
        public void Constructor_SetsCodeMessageAndData()
        {
            var error = new RpcError(1500, "test message", "test data");

            Assert.AreEqual((uint)1500, error.Code);
            Assert.AreEqual("test message", error.Message);
            Assert.AreEqual("test data", error.RpcData);
        }

        [Test]
        public void Constructor_DefaultDataIsNull()
        {
            var error = new RpcError(1500, "test message");

            Assert.IsNull(error.RpcData);
        }

        [Test]
        public void RpcError_IsException()
        {
            var error = new RpcError(1500, "test message");

            Assert.IsInstanceOf<System.Exception>(error);
            Assert.AreEqual("test message", error.Message);
        }
    }
}
