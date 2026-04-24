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

        
        AudioStream _audioStream;
        AudioSource _audioSource;

        TestRoomContext _context;
        
        private IEnumerator SetUp()
        {
            var sineWaveAudioSource = new SineWaveAudioSource();

            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "receiver";

            _context = new TestRoomContext(new[] { sender, receiver });
            var senderRoom = _context.Rooms[0];
            var subscriberRoom = _context.Rooms[1];

            var audioTrack = LocalAudioTrack.CreateAudioTrack(AudioTrackName, sineWaveAudioSource, senderRoom);

            yield return _context.ConnectAll();
            Assert.IsNull(_context.ConnectionError, _context.ConnectionError);

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

            sineWaveAudioSource.Start();
            yield return subscribedExp.Wait();
            Assert.IsNull(subscribedExp.Error);

            // We need an AudioListener in the scene in order for an AudioThread to read from sources 
            var listenerGO = new GameObject("LatencyTestAudioListener");
            var audioListener = listenerGO.AddComponent<AudioListener>();

            // This GO will be hooked up with the AudioStream
            var receiverGO = new GameObject("LatencyTestReceiver");
            _audioSource = receiverGO.AddComponent<AudioSource>();

            // AudioStream constructor adds AudioProbe and starts playback
            _audioStream = new AudioStream(subscribedTrack, _audioSource);
        }
        

        
        [UnityTest, Category("E2E")]
        public IEnumerator AudioBuffer_FillLevelStaysStable()
        {
            yield return SetUp();

            // Fill buffer above prime within short time
            var bufferFillsAbovePrime = new Expectation(() => { return _audioStream.GetBufferFill() > 0.15f; }, 0.1f);            
            yield return bufferFillsAbovePrime.Wait();
            Assert.IsNull(bufferFillsAbovePrime.Error);

            // Buffer stays at stable levels over long time
            var elapsedTime = 0f;
            while (elapsedTime < 3f)
            {
                elapsedTime += Time.deltaTime;
                Assert.That(_audioStream.GetBufferFill(), Is.GreaterThan(0.10f));
                Assert.That(_audioStream.GetBufferFill(), Is.LessThan(0.8f));
                yield return null;
            }

            _context.Dispose();
        }

        [UnityTest, Category("E2E")]
        public IEnumerator AudioBuffer_CatchesUp_WhenAudioConsumptionPausedAndContinued()
        {
            yield return SetUp();

            // NORMAL BEHAVIOUR
            var elapsed = 0f;
            while (elapsed < 1f)
            {                                                                           
                var fill = _audioStream.GetBufferFill();
                Debug.Log($"[AudioStream] t={elapsed:F2}s  filled={fill:P1}");
                elapsed += Time.deltaTime;
                yield return null;       
            }

            // The AudioBuffer should not be much filled
            Assert.That(_audioStream.GetBufferFill(), Is.LessThan(0.7));

            // BACKGROUNDED
            Debug.Log("Mock backgrounding");
            MockApplicationBackgrounded();
            
            // Buffer is full at this point
            var BufferFillsInBackgroundExpectation = new Expectation(() => { return _audioStream.GetBufferFill() == 1f; });            
            yield return BufferFillsInBackgroundExpectation.Wait();
            Assert.IsNull(BufferFillsInBackgroundExpectation.Error);

            // Backgrounding
            MockApplicationForegrounded();
            
            // Buffer is cleared
            var fillAfterForegrounded = _audioStream.GetBufferFill();
            Assert.That(fillAfterForegrounded, Is.EqualTo(0f));

            // Buffer refills above primed threshold of 30ms, I give it 100ms for that
            var BufferRefillsUntilPrimed = new Expectation(() => { return _audioStream.GetBufferFill() >= 0.15f; }, 0.1f);
            yield return BufferFillsInBackgroundExpectation.Wait();
            Assert.IsNull(BufferFillsInBackgroundExpectation.Error);

            _context.Dispose();
        }

        private void MockApplicationBackgrounded()
        {            
            _audioSource.Pause();
            _audioStream.OnApplicationPause(true);
        }

        private void MockApplicationForegrounded()
        {            
            _audioSource.UnPause();
            _audioStream.OnApplicationPause(false);
        }
    }
}