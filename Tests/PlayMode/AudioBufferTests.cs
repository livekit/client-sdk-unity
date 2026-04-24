using System.Collections;
using UnityEngine.TestTools;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine;

namespace LiveKit.PlayModeTests
{
    class AudioBufferTests
    {
        const string AudioTrackName = "test-audio-track";
        static TrackPublishOptions AudioOptions() =>
            new TrackPublishOptions { Source = TrackSource.SourceMicrophone };

        [UnityTest]
        public IEnumerator AudioBuffer_CatchesUp_WhenAudioConsumptionPausedAndContinued_Manual()
        {
            // We need an AudioListener in the scene in order for an AudioThread to read from sources 
            var listenerGO = new GameObject("LatencyTestAudioListener");
            var audioListener = listenerGO.AddComponent<AudioListener>();

            // This GO will be hooked up with the AudioStream
            var receiverGO = new GameObject("LatencyTestReceiver");
            var unityAudioSource = receiverGO.AddComponent<AudioSource>();


            // AudioStream constructor adds AudioProbe and starts playback
            var audioStream = new AudioStream(null, unityAudioSource);

            yield return null;
        }

        [UnityTest, Category("E2E")]
        public IEnumerator AudioBuffer_CatchesUp_WhenAudioConsumptionPausedAndContinued()
        {
            var audioSource = new SineWaveAudioSource();

            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "receiver";

            using var context = new TestRoomContext(new[] { sender, receiver });
            var senderRoom = context.Rooms[0];
            var subscriberRoom = context.Rooms[1];

            var audioTrack = LocalAudioTrack.CreateAudioTrack(AudioTrackName, audioSource, senderRoom);

            yield return context.ConnectAll();
            Assert.IsNull(context.ConnectionError, context.ConnectionError);

            var publishAudioTrack = senderRoom.LocalParticipant.PublishTrack(audioTrack, AudioOptions());

            var subscribedExp = new Expectation(timeoutSeconds: 10);
            RemoteAudioTrack subscribedTrack = null;
            subscriberRoom.TrackSubscribed += (remoteTrack, publication, participant) =>
            {
                if (remoteTrack is RemoteAudioTrack remoteAudioTrack)
                {
                    subscribedTrack = remoteAudioTrack;
                    subscribedExp.Fulfill();    
                }
            };

            yield return publishAudioTrack;
            Assert.IsFalse(publishAudioTrack.IsError);

            audioSource.Start();
            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error);

            // We need an AudioListener in the scene in order for an AudioThread to read from sources 
            var listenerGO = new GameObject("LatencyTestAudioListener");
            var audioListener = listenerGO.AddComponent<AudioListener>();

            // This GO will be hooked up with the AudioStream
            var receiverGO = new GameObject("LatencyTestReceiver");
            var unityAudioSource = receiverGO.AddComponent<AudioSource>();

            // AudioStream constructor adds AudioProbe and starts playback
            var audioStream = new AudioStream(subscribedTrack, unityAudioSource);

            // NORMAL BEHAVIOUR
            var elapsed = 0f;
            while (elapsed < 1f)
            {                                                                           
                var fill = audioStream.GetBufferFill();
                Debug.Log($"[AudioStream] t={elapsed:F2}s  filled={fill:P1}");
                elapsed += Time.deltaTime;
                yield return null;       
            }

            // The AudioBuffer should not be much filled
            Assert.That(audioStream.GetBufferFill(), Is.LessThan(0.7));

            // BACKGROUNDED
            Debug.Log("Mock backgrounding");
            MockApplicationBackgrounded(unityAudioSource, audioStream);
            
            // Buffer is full at this point
            var BufferFillsInBackgroundExpectation = new Expectation(() => { return audioStream.GetBufferFill() == 1f; });            
            yield return BufferFillsInBackgroundExpectation.Wait();
            Assert.IsNull(BufferFillsInBackgroundExpectation.Error);

            // Backgrounding
            MockApplicationForegrounded(unityAudioSource, audioStream);
            
            // Buffer is cleared
            var fillAfterForegrounded = audioStream.GetBufferFill();
            Assert.That(fillAfterForegrounded, Is.EqualTo(0f));

            // Buffer refills above primed threshold of 30ms, I give it 100ms for that
            var BufferRefillsUntilPrimed = new Expectation(() => { return audioStream.GetBufferFill() >= 0.15f; }, 0.1f);
            yield return BufferFillsInBackgroundExpectation.Wait();
            Assert.IsNull(BufferFillsInBackgroundExpectation.Error);
        }

        private void MockApplicationBackgrounded(AudioSource audioSource, AudioStream audioStream)
        {            
            audioSource.Pause();
            audioStream.OnApplicationPause(true);
        }

        private void MockApplicationForegrounded(AudioSource audioSource, AudioStream audioStream)
        {            
            audioSource.UnPause();
            audioStream.OnApplicationPause(false);
        }
    }
}