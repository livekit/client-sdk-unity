using System.Collections;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    public class TrackTests
    {
        const string AudioTrackName = "test-audio-track";
        const string VideoTrackName = "test-video-track";

        static TrackPublishOptions AudioOptions() =>
            new TrackPublishOptions { Source = TrackSource.SourceMicrophone };

        static TrackPublishOptions VideoOptions() =>
            new TrackPublishOptions { Source = TrackSource.SourceCamera };

        static (TestRoomContext.ConnectionOptions publisher, TestRoomContext.ConnectionOptions subscriber) TwoPeers()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "track-publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "track-subscriber";
            return (publisher, subscriber);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PublishAudioTrack_UnpublishAndRepublish_Succeeds()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var room = context.Rooms[0];
            using var source = new SineWaveAudioSource();
            var track = LocalAudioTrack.CreateAudioTrack(AudioTrackName, source, room);

            var pub1 = room.LocalParticipant.PublishTrack(track, AudioOptions());
            yield return pub1;
            Assert.IsFalse(pub1.IsError, "first publish failed");

            var unpub = room.LocalParticipant.UnpublishTrack(track, stopOnUnpublish: false);
            yield return unpub;
            Assert.IsFalse(unpub.IsError, "unpublish failed");

            var pub2 = room.LocalParticipant.PublishTrack(track, AudioOptions());
            yield return pub2;
            Assert.IsFalse(pub2.IsError, "republish failed");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator SetMute_TrueFalse_TogglesSourceMuteState()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var room = context.Rooms[0];
            using var source = new SineWaveAudioSource();
            var track = LocalAudioTrack.CreateAudioTrack(AudioTrackName, source, room);
            var pub = room.LocalParticipant.PublishTrack(track, AudioOptions());
            yield return pub;
            Assert.IsFalse(pub.IsError);

            Assert.IsFalse(source.Muted, "source should start unmuted");

            ((ILocalTrack)track).SetMute(true);
            Assert.IsTrue(source.Muted, "source should be muted after SetMute(true)");

            ((ILocalTrack)track).SetMute(false);
            Assert.IsFalse(source.Muted, "source should be unmuted after SetMute(false)");
        }

        [UnityTest, Category("E2E")]
        public IEnumerator RemoteTrack_GetStats_ReturnsNoError()
        {
            var (publisher, subscriber) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            using var source = new SineWaveAudioSource();
            var localTrack = LocalAudioTrack.CreateAudioTrack(AudioTrackName, source, publisherRoom);

            var subscribedExp = new Expectation(timeoutSeconds: 10f);
            IRemoteTrack receivedRemoteTrack = null;
            subscriberRoom.TrackSubscribed += (remoteTrack, publication, participant) =>
            {
                receivedRemoteTrack = remoteTrack;
                subscribedExp.Fulfill();
            };

            var pub = publisherRoom.LocalParticipant.PublishTrack(localTrack, AudioOptions());
            yield return pub;
            Assert.IsFalse(pub.IsError);

            source.Start();
            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error);

            // Give the RTP pipeline a moment to collect measurements.
            yield return new WaitForSeconds(1.5f);

            var statsInstruction = receivedRemoteTrack.GetStats();
            yield return statsInstruction;

            Assert.IsFalse(statsInstruction.IsError, statsInstruction.Error);
            Assert.IsNotNull(statsInstruction.Stats);

            var hasLocalCandidate = false;
            var hasRemoteCandidate = false;
            var hasCandidatePair = false;
            var hasTransport = false;
            RtcStats.Types.InboundRtp inboundRtp = null;
            foreach (var stat in statsInstruction.Stats)
            {
                switch (stat.StatsCase)
                {
                    case RtcStats.StatsOneofCase.LocalCandidate: hasLocalCandidate = true; break;
                    case RtcStats.StatsOneofCase.RemoteCandidate: hasRemoteCandidate = true; break;
                    case RtcStats.StatsOneofCase.CandidatePair: hasCandidatePair = true; break;
                    case RtcStats.StatsOneofCase.Transport: hasTransport = true; break;
                    case RtcStats.StatsOneofCase.InboundRtp: inboundRtp = stat.InboundRtp; break;
                }
            }

            Assert.IsTrue(hasLocalCandidate, "expected at least one LocalCandidate stat");
            Assert.IsTrue(hasRemoteCandidate, "expected at least one RemoteCandidate stat");
            Assert.IsTrue(hasCandidatePair, "expected at least one CandidatePair stat");
            Assert.IsTrue(hasTransport, "expected at least one Transport stat");
            Assert.IsNotNull(inboundRtp, "expected at least one InboundRtp stat");
            Assert.AreEqual("audio", inboundRtp.Stream.Kind);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator RemoteTrackPublication_SetVideoQuality_DoesNotThrow()
        {
            var (publisher, subscriber) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            var videoSource = new StubVideoSource();
            var localTrack = LocalVideoTrack.CreateVideoTrack(VideoTrackName, videoSource, publisherRoom);

            // Video track uses a stub source that never pushes frames. TrackSubscribed may not
            // fire in that case, but the RemoteTrackPublication still propagates via TrackPublished.
            var publishedExp = new Expectation(timeoutSeconds: 10f);
            RemoteTrackPublication receivedPublication = null;
            subscriberRoom.TrackPublished += (publication, participant) =>
            {
                receivedPublication = publication;
                publishedExp.Fulfill();
            };

            var pub = publisherRoom.LocalParticipant.PublishTrack(localTrack, VideoOptions());
            yield return pub;
            Assert.IsFalse(pub.IsError);

            yield return publishedExp.Wait();
            Assert.IsNull(publishedExp.Error);
            Assert.IsNotNull(receivedPublication);

            Assert.DoesNotThrow(() => receivedPublication.SetVideoQuality(VideoQuality.Low));
            Assert.DoesNotThrow(() => receivedPublication.SetVideoQuality(VideoQuality.High));
        }

        [UnityTest, Category("E2E")]
        public IEnumerator RemoteTrackPublication_Unsubscribe_UpdatesFlagAndTriggersEvent()
        {
            var (publisher, subscriber) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            using var source = new SineWaveAudioSource();
            var localTrack = LocalAudioTrack.CreateAudioTrack(AudioTrackName, source, publisherRoom);

            var subscribedExp = new Expectation(timeoutSeconds: 10f);
            RemoteTrackPublication receivedPublication = null;
            subscriberRoom.TrackSubscribed += (remoteTrack, publication, participant) =>
            {
                receivedPublication = publication;
                subscribedExp.Fulfill();
            };

            var unsubscribedExp = new Expectation(timeoutSeconds: 10f);
            RemoteTrackPublication unsubscribedPublication = null;
            subscriberRoom.TrackUnsubscribed += (remoteTrack, publication, participant) =>
            {
                unsubscribedPublication = publication;
                unsubscribedExp.Fulfill();
            };

            var pub = publisherRoom.LocalParticipant.PublishTrack(localTrack, AudioOptions());
            yield return pub;
            Assert.IsFalse(pub.IsError);

            source.Start();
            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error);
            Assert.IsNotNull(receivedPublication);

            receivedPublication.SetSubscribed(false);
            Assert.IsFalse(receivedPublication.Subscribed);

            yield return unsubscribedExp.Wait();
            Assert.IsNull(unsubscribedExp.Error);
            Assert.AreSame(receivedPublication, unsubscribedPublication);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator RemoteTrack_SetEnabled_FalseAndTrue_DoesNotThrow()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "enable-remote-publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "enable-remote-subscriber";

            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            using var source = new SineWaveAudioSource();
            var localTrack = LocalAudioTrack.CreateAudioTrack("enable-remote-audio", source, publisherRoom);

            var subscribedExp = new Expectation(timeoutSeconds: 10f);
            IRemoteTrack receivedRemoteTrack = null;
            subscriberRoom.TrackSubscribed += (remoteTrack, publication, participant) =>
            {
                receivedRemoteTrack = remoteTrack;
                subscribedExp.Fulfill();
            };

            var pub = publisherRoom.LocalParticipant.PublishTrack(
                localTrack,
                new TrackPublishOptions { Source = TrackSource.SourceMicrophone });
            yield return pub;
            Assert.IsFalse(pub.IsError);

            source.Start();
            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error);
            Assert.IsNotNull(receivedRemoteTrack);

            Assert.DoesNotThrow(() => receivedRemoteTrack.SetEnabled(false));
            Assert.DoesNotThrow(() => receivedRemoteTrack.SetEnabled(true));
        }
    }
}
