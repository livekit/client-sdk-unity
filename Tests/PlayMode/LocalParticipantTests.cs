using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;

namespace LiveKit.PlayModeTests
{
    public class LocalParticipantTests
    {
        [UnityTest, Category("E2E")]
        public IEnumerator Sid_StartsWithPA()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Identity = "test-identity";

            using var context = new TestRoomContext(options);
            yield return context.ConnectAll();
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

            var localParticipant = context.Rooms[0].LocalParticipant;
            StringAssert.StartsWith("PA_", localParticipant.Sid);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Identity_MatchesProvided()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Identity = "test-identity";

            using var context = new TestRoomContext(options);
            yield return context.ConnectAll();
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

            var localParticipant = context.Rooms[0].LocalParticipant;
            Assert.AreEqual(options.Identity, localParticipant.Identity);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Name_MatchesProvided()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.DisplayName = "test-display-name";

            using var context = new TestRoomContext(options);
            yield return context.ConnectAll();
            if (context.ConnectionError != null) Assert.Fail(context.ConnectionError);

            var localParticipant = context.Rooms[0].LocalParticipant;
            Assert.AreEqual(options.DisplayName, localParticipant.Name);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Metadata_MatchesProvided()
        {
            var options = TestRoomContext.ConnectionOptions.Default;
            options.Metadata = "test-metadata";

            using var context = new TestRoomContext(options);
            yield return context.ConnectAll();

            var localParticipant = context.Rooms[0].LocalParticipant;
            Assert.AreEqual(options.Metadata, localParticipant.Metadata);
        }
    }
}
