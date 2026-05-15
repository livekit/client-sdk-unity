using System.Collections;
using System.Collections.Generic;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// Reproduces the late-join FFI race: a publisher publishes tracks first,
    /// then a consumer connects with TrackSubscribed already wired. The consumer
    /// must receive TrackSubscribed for every track that was already published
    /// at the moment of its connect — events emitted by Rust between the
    /// ConnectCallback and the client's listener registration must not be
    /// dropped. This is gated by the ReadyForRoomEvent FFI handshake.
    /// </summary>
    public class LateJoinTrackSubscriptionTests
    {
        const int AudioTrackCount = 2;
        const int VideoTrackCount = 2;
        const float SubscribeTimeoutSeconds = 15f;

        static TrackPublishOptions AudioOptions() =>
            new TrackPublishOptions { Source = TrackSource.SourceMicrophone };

        static TrackPublishOptions VideoOptions() =>
            new TrackPublishOptions { Source = TrackSource.SourceCamera, Simulcast = false };

        static (TestRoomContext.ConnectionOptions publisher, TestRoomContext.ConnectionOptions subscriber) TwoPeers()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "late-join-publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "late-join-subscriber";
            return (publisher, subscriber);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator LateJoiner_ReceivesTrackSubscribedForAlreadyPublishedTracks()
        {
            var (publisherOptions, subscriberOptions) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisherOptions, subscriberOptions });

            // 1. Publisher connects first.
            yield return context.ConnectRoom(0);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];
            var publisherIdentity = publisherRoom.LocalParticipant.Identity;

            // 2. Publisher publishes audio + video tracks BEFORE the consumer joins.
            var expectedTrackNames = new HashSet<string>();
            var audioSources = new List<SineWaveAudioSource>();
            var videoSources = new List<StubVideoSource>();

            for (int i = 0; i < AudioTrackCount; i++)
            {
                var trackName = $"late-join-audio-{i}";
                var source = new SineWaveAudioSource();
                audioSources.Add(source);
                var localTrack = LocalAudioTrack.CreateAudioTrack(trackName, source, publisherRoom);
                var pub = publisherRoom.LocalParticipant.PublishTrack(localTrack, AudioOptions());
                yield return pub;
                Assert.IsFalse(pub.IsError, $"publish failed for {trackName}");
                expectedTrackNames.Add(trackName);
            }

            for (int i = 0; i < VideoTrackCount; i++)
            {
                var trackName = $"late-join-video-{i}";
                var source = new StubVideoSource();
                videoSources.Add(source);
                var localTrack = LocalVideoTrack.CreateVideoTrack(trackName, source, publisherRoom);
                var pub = publisherRoom.LocalParticipant.PublishTrack(localTrack, VideoOptions());
                yield return pub;
                Assert.IsFalse(pub.IsError, $"publish failed for {trackName}");
                expectedTrackNames.Add(trackName);
            }

            // 3. Wire subscriber's TrackSubscribed handler BEFORE connecting.
            //    This is the realistic late-join usage pattern; the handler must
            //    fire for every snapshot publication the late joiner sees.
            var subscribedNames = new HashSet<string>();
            var subscribedKinds = new Dictionary<string, TrackKind>();
            var subscribedCounts = new Dictionary<string, int>();
            var subscribedExpectation = new Expectation(
                predicate: () =>
                {
                    lock (subscribedNames) return subscribedNames.Count >= expectedTrackNames.Count;
                },
                timeoutSeconds: SubscribeTimeoutSeconds);

            subscriberRoom.TrackSubscribed += (track, publication, participant) =>
            {
                lock (subscribedNames)
                {
                    subscribedNames.Add(publication.Name);
                    subscribedKinds[publication.Name] = publication.Kind;
                    subscribedCounts.TryGetValue(publication.Name, out var count);
                    subscribedCounts[publication.Name] = count + 1;
                }
            };

            // 4. Subscriber joins late.
            yield return context.ConnectRoom(1);
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            // 5. Wait for all expected TrackSubscribed events to arrive.
            yield return subscribedExpectation.Wait();
            Assert.IsNull(subscribedExpectation.Error,
                $"Timed out before all late-join subscriptions arrived. " +
                $"Received: [{string.Join(", ", subscribedNames)}] / " +
                $"Expected: [{string.Join(", ", expectedTrackNames)}]");

            // 6. Each expected track was subscribed exactly once with the right kind.
            foreach (var name in expectedTrackNames)
            {
                Assert.IsTrue(subscribedNames.Contains(name),
                    $"Missing TrackSubscribed event for {name}");
                Assert.AreEqual(1, subscribedCounts[name],
                    $"Expected exactly one TrackSubscribed event for {name}, got {subscribedCounts[name]}");

                var expectedKind = name.StartsWith("late-join-audio")
                    ? TrackKind.KindAudio
                    : TrackKind.KindVideo;
                Assert.AreEqual(expectedKind, subscribedKinds[name],
                    $"Wrong kind for subscribed track {name}");
            }

            // 7. The remote-participant snapshot on the subscriber side reflects
            //    the publisher's publications.
            Assert.IsTrue(subscriberRoom.RemoteParticipants.TryGetValue(publisherIdentity, out var remotePublisher),
                $"Subscriber did not see remote participant {publisherIdentity}");

            var snapshotNames = new HashSet<string>();
            foreach (var pub in remotePublisher.Tracks.Values)
                snapshotNames.Add(pub.Name);

            foreach (var name in expectedTrackNames)
            {
                Assert.IsTrue(snapshotNames.Contains(name),
                    $"Subscriber's remote participant snapshot is missing publication {name}; " +
                    $"snapshot: [{string.Join(", ", snapshotNames)}]");
            }

            foreach (var s in audioSources) s.Dispose();
        }
    }
}
