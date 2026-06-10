using System.Collections;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// End-to-end tests that publish a platform-ADM-backed audio track to a second
    /// participant via a local LiveKit server. Skipped (Assert.Ignore) when no platform
    /// ADM is available. Mirrors the C++ PlatformAudioIntegrationTest suite.
    /// </summary>
    public class PlatformAudioIntegrationTests
    {
        static TrackPublishOptions MicOptions() =>
            new TrackPublishOptions { Source = TrackSource.SourceMicrophone };

        static (TestRoomContext.ConnectionOptions publisher, TestRoomContext.ConnectionOptions subscriber) TwoPeers()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "platform-audio-publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "platform-audio-subscriber";
            return (publisher, subscriber);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PublishPlatformAudioTrack_EndToEnd()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            var (publisher, subscriber) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            using var source = new PlatformAudioSource(platformAudio);
            const string trackName = "platform-mic";
            var track = LocalAudioTrack.CreateAudioTrack(trackName, source, publisherRoom);

            var subscribedExp = new Expectation(timeoutSeconds: 20f);
            subscriberRoom.TrackSubscribed += (remoteTrack, publication, participant) =>
            {
                if (remoteTrack.Kind == TrackKind.KindAudio && publication.Name == trackName)
                    subscribedExp.Fulfill();
            };

            var pub = publisherRoom.LocalParticipant.PublishTrack(track, MicOptions());
            yield return pub;
            Assert.IsFalse(pub.IsError, "publish failed");

            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error, "receiver never subscribed to the platform audio track");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator UnpublishPlatformAudioTrack_Propagates()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            var (publisher, subscriber) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            using var source = new PlatformAudioSource(platformAudio);
            const string trackName = "platform-mic-unpublish";
            var track = LocalAudioTrack.CreateAudioTrack(trackName, source, publisherRoom);

            var subscribedExp = new Expectation(timeoutSeconds: 20f);
            subscriberRoom.TrackSubscribed += (remoteTrack, publication, participant) =>
            {
                if (remoteTrack.Kind == TrackKind.KindAudio && publication.Name == trackName)
                    subscribedExp.Fulfill();
            };

            var removedExp = new Expectation(timeoutSeconds: 20f);
            subscriberRoom.TrackUnsubscribed += (_, _, _) => removedExp.Fulfill();
            subscriberRoom.TrackUnpublished += (_, _) => removedExp.Fulfill();

            var pub = publisherRoom.LocalParticipant.PublishTrack(track, MicOptions());
            yield return pub;
            Assert.IsFalse(pub.IsError, "publish failed");

            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error, "receiver never subscribed to the platform audio track");

            var unpub = publisherRoom.LocalParticipant.UnpublishTrack(track, stopOnUnpublish: false);
            yield return unpub;
            Assert.IsFalse(unpub.IsError, "unpublish failed");

            yield return removedExp.Wait();
            Assert.IsNull(removedExp.Error, "receiver never saw the platform audio track removed");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator MultipleSourcesFromOneManager_Publish()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            var (publisher, subscriber) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            using var sourceA = new PlatformAudioSource(platformAudio);
            using var sourceB = new PlatformAudioSource(platformAudio);
            var handleA = (long)sourceA.Handle.DangerousGetHandle();
            var handleB = (long)sourceB.Handle.DangerousGetHandle();
            Assert.AreNotEqual(0, handleA, "source A handle should be non-zero");
            Assert.AreNotEqual(0, handleB, "source B handle should be non-zero");
            Assert.AreNotEqual(handleA, handleB, "sources should have distinct FFI handles");

            const string nameA = "platform-mic-a";
            const string nameB = "platform-mic-b";
            var trackA = LocalAudioTrack.CreateAudioTrack(nameA, sourceA, publisherRoom);
            var trackB = LocalAudioTrack.CreateAudioTrack(nameB, sourceB, publisherRoom);

            var subscribedA = new Expectation(timeoutSeconds: 20f);
            var subscribedB = new Expectation(timeoutSeconds: 20f);
            subscriberRoom.TrackSubscribed += (remoteTrack, publication, participant) =>
            {
                if (publication.Name == nameA) subscribedA.Fulfill();
                else if (publication.Name == nameB) subscribedB.Fulfill();
            };

            var pubA = publisherRoom.LocalParticipant.PublishTrack(trackA, MicOptions());
            yield return pubA;
            Assert.IsFalse(pubA.IsError, "publish A failed");

            var pubB = publisherRoom.LocalParticipant.PublishTrack(trackB, MicOptions());
            yield return pubB;
            Assert.IsFalse(pubB.IsError, "publish B failed");

            yield return subscribedA.Wait();
            Assert.IsNull(subscribedA.Error, "receiver did not subscribe to track A");
            yield return subscribedB.Wait();
            Assert.IsNull(subscribedB.Error, "receiver did not subscribe to track B");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PlatformAudioFramesReachRemote_ViaStats()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            var (publisher, subscriber) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            using var source = new PlatformAudioSource(platformAudio);
            const string trackName = "platform-mic-frames";
            var track = LocalAudioTrack.CreateAudioTrack(trackName, source, publisherRoom);

            var subscribedExp = new Expectation(timeoutSeconds: 20f);
            IRemoteTrack receivedRemoteTrack = null;
            subscriberRoom.TrackSubscribed += (remoteTrack, publication, participant) =>
            {
                if (remoteTrack.Kind == TrackKind.KindAudio && publication.Name == trackName)
                {
                    receivedRemoteTrack = remoteTrack;
                    subscribedExp.Fulfill();
                }
            };

            var pub = publisherRoom.LocalParticipant.PublishTrack(track, MicOptions());
            yield return pub;
            Assert.IsFalse(pub.IsError, "publish failed");

            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error, "receiver never subscribed to the platform audio track");
            Assert.IsNotNull(receivedRemoteTrack);

            // Give the RTP pipeline a moment to collect inbound measurements. Unity's audio
            // subsystem does not deliver decoded frames in -batchmode, so we assert media flow
            // via an InboundRtp audio stat (the headless-friendly equivalent of the C++ frame
            // callback test).
            yield return new WaitForSeconds(1.5f);

            var statsInstruction = receivedRemoteTrack.GetStats();
            yield return statsInstruction;
            Assert.IsFalse(statsInstruction.IsError, statsInstruction.Error);
            Assert.IsNotNull(statsInstruction.Stats);

            RtcStats.Types.InboundRtp inboundRtp = null;
            foreach (var stat in statsInstruction.Stats)
            {
                if (stat.StatsCase == RtcStats.StatsOneofCase.InboundRtp)
                {
                    inboundRtp = stat.InboundRtp;
                    break;
                }
            }

            Assert.IsNotNull(inboundRtp, "expected an InboundRtp stat for the platform audio track");
            Assert.AreEqual("audio", inboundRtp.Stream.Kind);
        }
    }
}
