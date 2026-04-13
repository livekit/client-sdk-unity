using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;
using static LiveKit.PlayModeTests.Utils.TimeoutExtensions;

namespace LiveKit.PlayModeTests
{
    public class DataTrackTests
    {
        const string TestTrackName = "test-track";

        /// <summary>
        /// Reads a frame from the stream while pushing repeatedly via the supplied action.
        /// The SFU data path may not be ready immediately after Subscribe(), so early
        /// pushes can be silently dropped. This retries until a frame arrives or the
        /// timeout is reached.
        /// </summary>
        static IEnumerator PushUntilReceived(
            DataTrackStream.ReadFrameInstruction instruction,
            Action push,
            float timeoutSeconds = 10f,
            float intervalSeconds = 0.2f)
        {
            var start = Time.realtimeSinceStartup;
            var lastPush = -1f;
            while (instruction.keepWaiting)
            {
                var elapsed = Time.realtimeSinceStartup - start;
                if (elapsed > timeoutSeconds)
                    Assert.Fail($"Timed out after {timeoutSeconds}s waiting for data track frame");
                if (elapsed - lastPush >= intervalSeconds)
                {
                    try { push(); }
                    catch (PushFrameError) { }
                    lastPush = elapsed;
                }
                yield return null;
            }
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PublishDataTrack_Succeeds()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var room = context.Rooms[0];
            var publishInstruction = room.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();

            Assert.IsFalse(publishInstruction.IsError);
            Assert.IsNotNull(publishInstruction.Track);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PublishDataTrack_TrackInfoMatchesOptions()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var room = context.Rooms[0];
            var publishInstruction = room.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();

            Assert.IsFalse(publishInstruction.IsError);
            var track = publishInstruction.Track;
            Assert.AreEqual(TestTrackName, track.Info.Name);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PublishDataTrack_IsPublishedReturnsTrue()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var room = context.Rooms[0];
            var publishInstruction = room.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();

            Assert.IsFalse(publishInstruction.IsError);
            Assert.IsTrue(publishInstruction.Track.IsPublished());
        }

        [UnityTest, Category("E2E")]
        public IEnumerator TryPush_Succeeds()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var room = context.Rooms[0];
            var publishInstruction = room.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();

            Assert.IsFalse(publishInstruction.IsError);
            var track = publishInstruction.Track;

            Assert.DoesNotThrow(() =>
            {
                track.TryPush(new DataTrackFrame(new byte[] { 0x01, 0x02, 0x03 }));
            });
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Unpublish_IsPublishedReturnsFalse()
        {
            using var context = new TestRoomContext();
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var room = context.Rooms[0];
            var publishInstruction = room.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();

            Assert.IsFalse(publishInstruction.IsError);
            var track = publishInstruction.Track;
            Assert.IsTrue(track.IsPublished());

            track.Unpublish();
            Assert.IsFalse(track.IsPublished());
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PublishAndUnpublish_TriggersEvents()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "subscriber";

            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var subscriberRoom = context.Rooms[1];
            var publishedExpectation = new Expectation(timeoutSeconds: 10f);
            RemoteDataTrack receivedTrack = null;

            subscriberRoom.DataTrackPublished += (track) =>
            {
                receivedTrack = track;
                publishedExpectation.Fulfill();
            };

            var publisherRoom = context.Rooms[0];
            var publishInstruction = publisherRoom.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();
            Assert.IsFalse(publishInstruction.IsError);

            yield return publishedExpectation.Wait();
            Assert.IsNull(publishedExpectation.Error);
            Assert.AreEqual(TestTrackName, receivedTrack.Info.Name);
            Assert.AreEqual(publisher.Identity, receivedTrack.PublisherIdentity);

            var unpublishedExpectation = new Expectation(timeoutSeconds: 10f);
            string unpublishedSid = null;

            subscriberRoom.DataTrackUnpublished += (sid) =>
            {
                unpublishedSid = sid;
                unpublishedExpectation.Fulfill();
            };

            publishInstruction.Track.Unpublish();

            yield return unpublishedExpectation.Wait();
            Assert.IsNull(unpublishedExpectation.Error);
            Assert.AreEqual(receivedTrack.Info.Sid, unpublishedSid);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator Subscribe_Succeeds()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "subscriber";

            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var subscriberRoom = context.Rooms[1];
            var trackExpectation = new Expectation(timeoutSeconds: 10f);
            RemoteDataTrack remoteTrack = null;

            subscriberRoom.DataTrackPublished += (track) =>
            {
                remoteTrack = track;
                trackExpectation.Fulfill();
            };

            var publisherRoom = context.Rooms[0];
            var publishInstruction = publisherRoom.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();
            Assert.IsFalse(publishInstruction.IsError);

            yield return trackExpectation.Wait();
            Assert.IsNull(trackExpectation.Error);

            var stream = remoteTrack.Subscribe();
            Assert.IsNotNull(stream);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PushAndReceive_FramePayloadMatches()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "subscriber";

            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var subscriberRoom = context.Rooms[1];
            var trackExpectation = new Expectation(timeoutSeconds: 10f);
            RemoteDataTrack remoteTrack = null;

            subscriberRoom.DataTrackPublished += (track) =>
            {
                remoteTrack = track;
                trackExpectation.Fulfill();
            };

            var publisherRoom = context.Rooms[0];
            var publishInstruction = publisherRoom.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();
            Assert.IsFalse(publishInstruction.IsError);

            yield return trackExpectation.Wait();
            Assert.IsNull(trackExpectation.Error);

            var stream = remoteTrack.Subscribe();
            var sentPayload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

            var frameInstruction = stream.ReadFrame();
            yield return PushUntilReceived(frameInstruction,
                () => publishInstruction.Track.TryPush(new DataTrackFrame(sentPayload)));

            Assert.IsNull(frameInstruction.Error);
            Assert.IsTrue(frameInstruction.IsCurrentReadDone, "Should have received a frame");
            Assert.AreEqual(sentPayload, frameInstruction.Frame.Payload);
        }

        [UnityTest, Category("E2E")]
        public IEnumerator PushAndReceive_TimestampIsPreserved()
        {
            var publisher = TestRoomContext.ConnectionOptions.Default;
            publisher.Identity = "publisher";
            var subscriber = TestRoomContext.ConnectionOptions.Default;
            subscriber.Identity = "subscriber";

            using var context = new TestRoomContext(new[] { publisher, subscriber });
            yield return context.ConnectAll().WithTimeout();
            Assert.IsNull(context.ConnectionError);

            var subscriberRoom = context.Rooms[1];
            var trackExpectation = new Expectation(timeoutSeconds: 10f);
            RemoteDataTrack remoteTrack = null;

            subscriberRoom.DataTrackPublished += (track) =>
            {
                remoteTrack = track;
                trackExpectation.Fulfill();
            };

            var publisherRoom = context.Rooms[0];
            var publishInstruction = publisherRoom.LocalParticipant.PublishDataTrack(TestTrackName);
            yield return publishInstruction.WithTimeout();
            Assert.IsFalse(publishInstruction.IsError);

            yield return trackExpectation.Wait();
            Assert.IsNull(trackExpectation.Error);

            var stream = remoteTrack.Subscribe();

            var frameInstruction = stream.ReadFrame();
            yield return PushUntilReceived(frameInstruction,
                () => publishInstruction.Track.TryPush(
                    new DataTrackFrame(new byte[] { 0x01 }).WithUserTimestampNow()));

            Assert.IsNull(frameInstruction.Error);
            Assert.IsTrue(frameInstruction.IsCurrentReadDone, "Should have received a frame");
            Assert.IsNotNull(frameInstruction.Frame.UserTimestamp, "Timestamp should be preserved");

            var latency = frameInstruction.Frame.DurationSinceTimestamp();
            Assert.IsNotNull(latency);
            Assert.Less(latency.Value, 5.0, "Latency should be reasonable");
        }

        [Test]
        public void DataTrackFrame_WithUserTimestampNow_SetsTimestamp()
        {
            var frame = new DataTrackFrame(new byte[] { 0x01, 0x02 });
            Assert.IsNull(frame.UserTimestamp);

            var stamped = frame.WithUserTimestampNow();
            Assert.IsNotNull(stamped.UserTimestamp);
            Assert.AreEqual(frame.Payload, stamped.Payload);
        }

        [Test]
        public void DataTrackFrame_DurationSinceTimestamp_NullWithoutTimestamp()
        {
            var frame = new DataTrackFrame(new byte[] { 0x01 });
            Assert.IsNull(frame.DurationSinceTimestamp());
        }

        [Test]
        public void DataTrackFrame_DurationSinceTimestamp_ReturnsValue()
        {
            var frame = new DataTrackFrame(new byte[] { 0x01 }).WithUserTimestampNow();
            var duration = frame.DurationSinceTimestamp();
            Assert.IsNotNull(duration);
            Assert.GreaterOrEqual(duration.Value, 0.0);
        }
    }
}
