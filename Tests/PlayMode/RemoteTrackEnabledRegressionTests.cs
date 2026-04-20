using System.Collections;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>    
    /// This test publishes an audio track from a second room and exercises the FFI
    /// round-trip on the subscriber side.
    /// </summary>
    public class RemoteTrackEnabledTests
    {
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
