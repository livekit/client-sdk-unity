using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;

namespace LiveKit.PlayModeTests
{
    public class ParticipantMetadataTests
    {
        [UnityTest, Category("E2E")]
        public IEnumerator SetMetadata_UpdatesLocalAndTriggersRemoteEvent()
        {
            var first = TestRoomContext.ConnectionOptions.Default;
            first.Identity = "first";
            first.CanUpdateOwnMetadata = true;
            var second = TestRoomContext.ConnectionOptions.Default;
            second.Identity = "second";

            using var context = new TestRoomContext(new[] { first, second });

            string receivedMetadata = null;
            var metadataExpectation = new Expectation(timeoutSeconds: 10f);
            context.Rooms[1].ParticipantMetadataChanged += (participant) =>
            {
                if (participant.Identity == first.Identity)
                {
                    receivedMetadata = participant.Metadata;
                    metadataExpectation.Fulfill();
                }
            };

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var setInstruction = context.Rooms[0].LocalParticipant.SetMetadata("new-metadata");
            yield return setInstruction;
            Assert.IsFalse(setInstruction.IsError, "SetMetadata failed");

            yield return metadataExpectation.Wait();
            Assert.IsNull(metadataExpectation.Error, metadataExpectation.Error);
            Assert.AreEqual("new-metadata", receivedMetadata);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator SetName_UpdatesLocalAndTriggersRemoteEvent()
        {
            var first = TestRoomContext.ConnectionOptions.Default;
            first.Identity = "first";
            first.CanUpdateOwnMetadata = true;
            var second = TestRoomContext.ConnectionOptions.Default;
            second.Identity = "second";

            using var context = new TestRoomContext(new[] { first, second });

            string receivedName = null;
            var nameExpectation = new Expectation(timeoutSeconds: 10f);
            context.Rooms[1].ParticipantNameChanged += (participant) =>
            {
                if (participant.Identity == first.Identity)
                {
                    receivedName = participant.Name;
                    nameExpectation.Fulfill();
                }
            };

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var setInstruction = context.Rooms[0].LocalParticipant.SetName("new-display-name");
            yield return setInstruction;
            Assert.IsFalse(setInstruction.IsError, "SetName failed");

            yield return nameExpectation.Wait();
            Assert.IsNull(nameExpectation.Error, nameExpectation.Error);
            Assert.AreEqual("new-display-name", receivedName);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator SetAttributes_UpdatesLocalAndTriggersRemoteEvent()
        {
            var first = TestRoomContext.ConnectionOptions.Default;
            first.Identity = "first";
            first.CanUpdateOwnMetadata = true;
            var second = TestRoomContext.ConnectionOptions.Default;
            second.Identity = "second";

            using var context = new TestRoomContext(new[] { first, second });

            var attributesExpectation = new Expectation(timeoutSeconds: 10f);
            IDictionary<string, string> receivedAttributes = null;
            context.Rooms[1].ParticipantAttributesChanged += (participant) =>
            {
                if (participant.Identity == first.Identity)
                {
                    receivedAttributes = new Dictionary<string, string>(participant.Attributes);
                    attributesExpectation.Fulfill();
                }
            };

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var attrs = new Dictionary<string, string> { { "key1", "value1" }, { "key2", "value2" } };
            var setInstruction = context.Rooms[0].LocalParticipant.SetAttributes(attrs);
            yield return setInstruction;
            Assert.IsFalse(setInstruction.IsError, "SetAttributes failed");

            yield return attributesExpectation.Wait();
            Assert.IsNull(attributesExpectation.Error, attributesExpectation.Error);
            Assert.IsNotNull(receivedAttributes);
            Assert.AreEqual("value1", receivedAttributes["key1"]);
            Assert.AreEqual("value2", receivedAttributes["key2"]);
        }
    }
}
