using System.Collections;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    public class VideoFrameMetadataTests
    {
        const string VideoTrackName = "metadata-video-track";

        static (TestRoomContext.ConnectionOptions publisher, TestRoomContext.ConnectionOptions subscriber) TwoPeers()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "metadata-publisher";
            // Step 1 diagnostic: dynacast (default-on) handles SubscribedQualityUpdate by
            // calling sender.set_parameters(), which drops the sender's encoder->packetizer
            // frame-metadata transformer. Disabling it on the publisher should let metadata
            // arrive. See rust-sdks #1003.
            publisher.Dynacast = false;
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "metadata-subscriber";
            return (publisher, subscriber);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator VideoFrame_AttachedMetadata_ReceivedOnSubscriber()
        {
            var (publisher, subscriber) = TwoPeers();
            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publisherRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            const uint expectedFrameId = 42u;
            const ulong expectedUserTs = 0x1122334455667788UL;

            var source = new TestVideoSource(pushFrames: true);
            source.MetadataProvider = () => new FrameMetadata
            {
                FrameId = expectedFrameId,
                UserTimestamp = expectedUserTs,
            };

            var localTrack = LocalVideoTrack.CreateVideoTrack(VideoTrackName, source, publisherRoom);

            var subscribedExp = new Expectation(timeoutSeconds: 10f);
            RemoteVideoTrack receivedRemoteTrack = null;
            subscriberRoom.TrackSubscribed += (track, _, _) =>
            {
                if (track is RemoteVideoTrack rv)
                {
                    receivedRemoteTrack = rv;
                    subscribedExp.Fulfill();
                }
            };

            var options = new TrackPublishOptions
            {
                Source = TrackSource.SourceCamera,
            }.WithFrameMetadataFeatures(
                FrameMetadataFeature.FmfUserTimestamp,
                FrameMetadataFeature.FmfFrameId);
            var pub = publisherRoom.LocalParticipant.PublishTrack(localTrack, options);
            yield return pub;
            Assert.IsFalse(pub.IsError);

            // Host the source's Update coroutine on a throwaway MonoBehaviour so the
            // test body can yield on Expectations without owning the producer loop.
            var runnerObj = new GameObject("metadata-test-runner");
            var runner = runnerObj.AddComponent<CoroutineRunner>();
            source.Start();
            runner.StartCoroutine(source.Update());

            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error);
            Assert.IsNotNull(receivedRemoteTrack);

            var stream = new VideoStream(receivedRemoteTrack);
            var metadataExp = new Expectation(timeoutSeconds: 10f);
            FrameMetadata receivedMetadata = null;
            stream.FrameReceived += frame =>
            {
                if (receivedMetadata != null || frame.Metadata == null) return;
                receivedMetadata = frame.Metadata;
                metadataExp.Fulfill();
            };
            stream.Start();
            runner.StartCoroutine(stream.Update());

            yield return metadataExp.Wait();
            Assert.IsNull(metadataExp.Error, "expected a frame with metadata within timeout");
            Assert.IsNotNull(receivedMetadata);
            Assert.AreEqual(expectedFrameId, receivedMetadata.FrameId, "frame_id mismatch");
            Assert.AreEqual(expectedUserTs, receivedMetadata.UserTimestamp, "user_timestamp mismatch");

            source.Stop();
            stream.Stop();
            stream.Dispose();
            source.Dispose();
            Object.Destroy(runnerObj);
        }
    }
}
