using System;
using LiveKit.Internal.FFIClients;
using LiveKit.Proto;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class FfiResponseEnsureCleanTests
    {
        [Test]
        public void EnsureClean_DoesNotThrow_WhenMessageCaseIsNone()
        {
            var response = new FfiResponse();
            Assert.DoesNotThrow(() => response.EnsureClean());
        }

        [TestCase(nameof(FfiResponse.LocalTrackMute), typeof(LocalTrackMuteResponse))]
        [TestCase(nameof(FfiResponse.ByteStreamOpen), typeof(ByteStreamOpenResponse))]
        [TestCase(nameof(FfiResponse.SendStreamHeader), typeof(SendStreamHeaderResponse))]
        [TestCase(nameof(FfiResponse.SetRemoteTrackPublicationQuality), typeof(SetRemoteTrackPublicationQualityResponse))]
        [TestCase(nameof(FfiResponse.SendChatMessage), typeof(SendChatMessageResponse))]
        public void EnsureClean_Throws_ForOneofCases(string oneofProperty, Type messageType)
        {
            var response = new FfiResponse();
            var prop = typeof(FfiResponse).GetProperty(oneofProperty);
            Assert.NotNull(prop, $"FfiResponse.{oneofProperty} property not found");
            prop.SetValue(response, Activator.CreateInstance(messageType));

            Assert.Throws<InvalidOperationException>(() => response.EnsureClean());
        }

        [Test]
        public void EnsureClean_Throws_ForConnect()
        {
            var response = new FfiResponse { Connect = new ConnectResponse() };
            Assert.Throws<InvalidOperationException>(() => response.EnsureClean());
        }
    }
}
