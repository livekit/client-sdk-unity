using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiveKit.Proto;
using LiveKit.PlayModeTests.Utils;
using Debug = UnityEngine.Debug;

namespace LiveKit.PlayModeTests
{
    public class LatencyTests
    {
        // Audio configuration (matching C++ test)
        const int kAudioSampleRate = 48000;
        const int kAudioChannels = 1;
        const int kAudioFrameDurationMs = 10;
        const int kSamplesPerFrame = kAudioSampleRate * kAudioFrameDurationMs / 1000; // 480

        // Pulse frequency tagging
        const double kBaseFrequency = 500.0;   // Pulse 0 = 500 Hz
        const double kFrequencyStep = 100.0;   // Pulse i = 500 + i*100 Hz
        const double kMagnitudeThreshold = 0.15;
        const int kHighEnergyFramesPerPulse = 5;

        // Test parameters
        const int kTotalPulses = 10;
        const int kFramesBetweenPulses = 100; // ~1 second at 10ms/frame
        const float kEchoTimeoutSeconds = 2f;
        const int kConnectionTestIterations = 5;

        // =====================================================================
        // Test 1: Connection Time Measurement
        // =====================================================================

        [UnityTest, Category("E2E")]
        public IEnumerator ConnectionTime()
        {
            Debug.Log("\n=== Connection Time Measurement Test ===");
            Debug.Log($"Iterations: {kConnectionTestIterations}");

            var stats = new LatencyStats();

            for (int i = 0; i < kConnectionTestIterations; i++)
            {
                using var context = new TestRoomContext();
                var sw = Stopwatch.StartNew();
                yield return context.ConnectAll();
                sw.Stop();

                if (context.ConnectionError != null)
                {
                    Debug.Log($"  Iteration {i + 1}: FAILED to connect - {context.ConnectionError}");
                    continue;
                }

                double latencyMs = sw.Elapsed.TotalMilliseconds;
                stats.AddMeasurement(latencyMs);
                Debug.Log($"  Iteration {i + 1}: {latencyMs:F2} ms");

                // Small delay between iterations
                yield return new WaitForSeconds(0.5f);
            }

            stats.PrintStats("Connection Time Statistics");
            Assert.Greater(stats.Count, 0, "At least one connection should succeed");
        }

        // =====================================================================
        // Test 2: Audio Latency Measurement using Frequency-Tagged Pulses
        // =====================================================================

        [UnityTest, Category("E2E")]
        public IEnumerator AudioLatency()
        {
            Debug.Log("\n=== Audio Latency Measurement Test ===");
            Debug.Log("Using frequency-tagged pulses to measure audio round-trip latency");

            // --- Connect two participants ---
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "receiver";

            using var context = new TestRoomContext(new[] { receiver, sender });
            yield return context.ConnectAll();
            if (context.ConnectionError != null)
                Assert.Fail(context.ConnectionError);

            var receiverRoom = context.Rooms[0];
            var senderRoom = context.Rooms[1];
            Debug.Log($"Receiver connected as: {receiverRoom.LocalParticipant.Identity}");
            Debug.Log($"Sender connected as: {senderRoom.LocalParticipant.Identity}");

            // --- Wait for sender to be visible to receiver ---
            var participantExpectation = new Expectation(timeoutSeconds: 10f);
            if (receiverRoom.RemoteParticipants.ContainsKey(sender.Identity))
            {
                participantExpectation.Fulfill();
            }
            else
            {
                receiverRoom.ParticipantConnected += (p) =>
                {
                    if (p.Identity == sender.Identity)
                        participantExpectation.Fulfill();
                };
            }
            yield return participantExpectation.Wait();
            if (participantExpectation.Error != null)
                Assert.Fail($"Sender not visible to receiver: {participantExpectation.Error}");

            // --- Create and publish audio track from sender ---
            var audioSource = new TestAudioSource(channels: kAudioChannels);
            audioSource.Start();

            var audioTrack = LocalAudioTrack.CreateAudioTrack("latency-test", audioSource, senderRoom);
            var publishOptions = new TrackPublishOptions();
            var publish = senderRoom.LocalParticipant.PublishTrack(audioTrack, publishOptions);
            yield return publish;
            if (publish.IsError)
                Assert.Fail("Failed to publish audio track");

            Debug.Log("Audio track published, waiting for subscription...");

            // --- Wait for receiver to subscribe to the audio track ---
            RemoteAudioTrack subscribedTrack = null;
            var trackExpectation = new Expectation(timeoutSeconds: 10f);
            receiverRoom.TrackSubscribed += (track, publication, participant) =>
            {
                if (track is RemoteAudioTrack rat && participant.Identity == sender.Identity)
                {
                    subscribedTrack = rat;
                    trackExpectation.Fulfill();
                }
            };
            yield return trackExpectation.Wait();
            if (trackExpectation.Error != null)
                Assert.Fail($"Receiver did not subscribe to audio track: {trackExpectation.Error}");

            Debug.Log("Audio track subscribed, creating audio stream...");

            // --- Set up receiver audio stream + pulse detector ---
            var listenerGO = new GameObject("LatencyTestAudioListener");
            listenerGO.AddComponent<AudioListener>();

            var receiverGO = new GameObject("LatencyTestReceiver");
            var unityAudioSource = receiverGO.AddComponent<AudioSource>();
            unityAudioSource.spatialBlend = 0f;
            unityAudioSource.volume = 1f;

            // AudioStream constructor adds AudioProbe and starts playback
            var audioStream = new AudioStream(subscribedTrack, unityAudioSource);

            // Add PulseDetector AFTER AudioStream so OnAudioFilterRead order is correct
            var pulseDetector = receiverGO.AddComponent<AudioPulseDetector>();
            pulseDetector.TotalPulses = kTotalPulses;
            pulseDetector.BaseFrequency = kBaseFrequency;
            pulseDetector.FrequencyStep = kFrequencyStep;
            pulseDetector.MagnitudeThreshold = kMagnitudeThreshold;

            // --- Shared state for latency measurement ---
            var audioThreadStats = new LatencyStats();
            var frameReceivedStats = new LatencyStats();
            var lockObj = new object();
            var sendTimestamps = new long[kTotalPulses]; // per-pulse send timestamps
            var pulseReceived = new bool[kTotalPulses];  // track which pulses have been matched
            var frameReceivedPulse = new bool[kTotalPulses]; // track FrameReceived detections
            int missedPulses = 0;

            // FrameReceived callback (runs on main thread, before ring buffer)
            audioStream.FrameReceived += (frame) =>
            {
                int samples = (int)frame.SamplesPerChannel;
                int channels = (int)frame.NumChannels;
                int sampleRate = (int)frame.SampleRate;
                if (samples == 0) return;

                int bestPulse = -1;
                double bestMag = 0;

                for (int p = 0; p < kTotalPulses; p++)
                {
                    double freq = kBaseFrequency + p * kFrequencyStep;
                    double mag = GoertzelS16(frame.Data, channels, samples, sampleRate, freq);
                    if (mag > bestMag)
                    {
                        bestMag = mag;
                        bestPulse = p;
                    }
                }

                if (bestPulse < 0 || bestMag <= kMagnitudeThreshold)
                    return;

                lock (lockObj)
                {
                    if (bestPulse >= kTotalPulses || frameReceivedPulse[bestPulse])
                        return;
                    if (sendTimestamps[bestPulse] == 0)
                        return;

                    long receiveTimeTicks = Stopwatch.GetTimestamp();
                    double latencyMs = (receiveTimeTicks - sendTimestamps[bestPulse])
                        * 1000.0 / Stopwatch.Frequency;

                    if (latencyMs > 0 && latencyMs < 5000)
                    {
                        frameReceivedPulse[bestPulse] = true;
                        frameReceivedStats.AddMeasurement(latencyMs);
                        Debug.Log($"  Pulse {bestPulse} (FrameReceived): latency {latencyMs:F2} ms " +
                                  $"(freq: {kBaseFrequency + bestPulse * kFrequencyStep} Hz, mag: {bestMag:F3})");
                    }
                }
            };

            // Pulse detector callback (runs on audio thread)
            pulseDetector.PulseReceived += (pulseIndex, magnitude) =>
            {
                lock (lockObj)
                {
                    if (pulseIndex < 0 || pulseIndex >= kTotalPulses)
                        return;
                    if (pulseReceived[pulseIndex])
                        return; // already matched this pulse
                    if (sendTimestamps[pulseIndex] == 0)
                        return; // not sent yet

                    long receiveTimeTicks = Stopwatch.GetTimestamp();
                    double latencyMs = (receiveTimeTicks - sendTimestamps[pulseIndex])
                        * 1000.0 / Stopwatch.Frequency;

                    if (latencyMs > 0 && latencyMs < 5000)
                    {
                        pulseReceived[pulseIndex] = true;
                        audioThreadStats.AddMeasurement(latencyMs);
                        Debug.Log($"  Pulse {pulseIndex} (AudioThread): latency {latencyMs:F2} ms " +
                                  $"(freq: {kBaseFrequency + pulseIndex * kFrequencyStep} Hz, mag: {magnitude:F3})");
                    }
                }
            };

            // --- Send audio frames in real-time ---
            Debug.Log("Starting audio pulse transmission...");

            int frameCount = 0;
            int pulsesSent = 0;
            int highEnergyFramesRemaining = 0;
            double currentPulseFrequency = 0;
            var frameDuration = TimeSpan.FromMilliseconds(kAudioFrameDurationMs);
            var nextFrameTime = Stopwatch.GetTimestamp();

            while (pulsesSent < kTotalPulses)
            {
                // Wait until it's time to send the next frame
                long now = Stopwatch.GetTimestamp();
                double waitMs = (nextFrameTime - now) * 1000.0 / Stopwatch.Frequency;
                if (waitMs > 1.0)
                {
                    yield return null;
                    continue;
                }
                nextFrameTime += (long)(frameDuration.TotalSeconds * Stopwatch.Frequency);

                float[] frameData;

                if (highEnergyFramesRemaining > 0)
                {
                    frameData = GenerateToneFrame(kSamplesPerFrame, currentPulseFrequency);
                    highEnergyFramesRemaining--;
                }
                else if (frameCount % kFramesBetweenPulses == 0)
                {
                    // Start a new pulse with unique frequency
                    currentPulseFrequency = kBaseFrequency + pulsesSent * kFrequencyStep;
                    frameData = GenerateToneFrame(kSamplesPerFrame, currentPulseFrequency);
                    highEnergyFramesRemaining = kHighEnergyFramesPerPulse - 1;

                    lock (lockObj)
                    {
                        sendTimestamps[pulsesSent] = Stopwatch.GetTimestamp();
                    }
                    Debug.Log($"Sent pulse {pulsesSent}/{kTotalPulses} " +
                              $"(freq: {currentPulseFrequency} Hz, {kHighEnergyFramesPerPulse} frames)");
                    pulsesSent++;
                }
                else
                {
                    frameData = GenerateSilentFrame(kSamplesPerFrame);
                }

                audioSource.PushFrame(frameData, kAudioChannels, kAudioSampleRate);
                frameCount++;
            }

            // Wait for last echoes to arrive
            yield return new WaitForSeconds(kEchoTimeoutSeconds);

            // Count missed pulses
            lock (lockObj)
            {
                for (int i = 0; i < kTotalPulses; i++)
                {
                    if (sendTimestamps[i] > 0 && !pulseReceived[i])
                        missedPulses++;
                }
            }

            // --- Print results ---
            frameReceivedStats.PrintStats("Audio Latency (FrameReceived / main thread)");
            audioThreadStats.PrintStats("Audio Latency (AudioThread / OnAudioFilterRead)");
            if (missedPulses > 0)
                Debug.Log($"Missed pulses (timeout): {missedPulses}");

            // --- Cleanup ---
            audioStream.Dispose();
            UnityEngine.Object.Destroy(receiverGO);
            UnityEngine.Object.Destroy(listenerGO);
            audioSource.Stop();
            audioSource.Dispose();

            Assert.Greater(audioThreadStats.Count, 0, "At least one audio latency measurement should be recorded");
        }

        // =====================================================================
        // Test 3: Video Latency Measurement using Spatial Binary Encoding
        // =====================================================================

        // Video configuration
        const int kVideoWidth = 64;
        const int kVideoHeight = 64;
        const int kVideoStripThreshold = 128;
        const float kVideoPulseDurationSeconds = 0.5f;
        const float kVideoPulseIntervalSeconds = 2f;
        const float kVideoEchoTimeoutSeconds = 3f;
        const float kVideoWarmupSeconds = 3f;

        [UnityTest, Category("E2E")]
        public IEnumerator VideoLatency()
        {
            Debug.Log("\n=== Video Latency Measurement Test ===");
            Debug.Log("Using spatial binary encoding to measure video round-trip latency");

            // --- Connect two participants ---
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "video-sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "video-receiver";

            using var context = new TestRoomContext(new[] { receiver, sender });
            yield return context.ConnectAll();
            if (context.ConnectionError != null)
                Assert.Fail(context.ConnectionError);

            var receiverRoom = context.Rooms[0];
            var senderRoom = context.Rooms[1];
            Debug.Log($"Receiver connected as: {receiverRoom.LocalParticipant.Identity}");
            Debug.Log($"Sender connected as: {senderRoom.LocalParticipant.Identity}");

            // --- Wait for sender to be visible to receiver ---
            var participantExpectation = new Expectation(timeoutSeconds: 10f);
            if (receiverRoom.RemoteParticipants.ContainsKey(sender.Identity))
            {
                participantExpectation.Fulfill();
            }
            else
            {
                receiverRoom.ParticipantConnected += (p) =>
                {
                    if (p.Identity == sender.Identity)
                        participantExpectation.Fulfill();
                };
            }
            yield return participantExpectation.Wait();
            if (participantExpectation.Error != null)
                Assert.Fail($"Sender not visible to receiver: {participantExpectation.Error}");

            // --- Create and publish video track from sender ---
            var videoSource = new TestVideoSource(kVideoWidth, kVideoHeight);
            videoSource.Start();

            var videoTrack = LocalVideoTrack.CreateVideoTrack("video-latency-test", videoSource, senderRoom);
            var publishOptions = new TrackPublishOptions();
            var publish = senderRoom.LocalParticipant.PublishTrack(videoTrack, publishOptions);
            yield return publish;
            if (publish.IsError)
                Assert.Fail("Failed to publish video track");

            // Start the video source update coroutine (drives ReadBuffer + SendFrame each frame)
            var sourceUpdateCoroutine = TestCoroutineRunner.Start(videoSource.Update());

            Debug.Log("Video track published, waiting for subscription...");

            // --- Wait for receiver to subscribe to the video track ---
            RemoteVideoTrack subscribedTrack = null;
            var trackExpectation = new Expectation(timeoutSeconds: 10f);
            receiverRoom.TrackSubscribed += (track, publication, participant) =>
            {
                if (track is RemoteVideoTrack rvt && participant.Identity == sender.Identity)
                {
                    subscribedTrack = rvt;
                    trackExpectation.Fulfill();
                }
            };
            yield return trackExpectation.Wait();
            if (trackExpectation.Error != null)
                Assert.Fail($"Receiver did not subscribe to video track: {trackExpectation.Error}");

            Debug.Log("Video track subscribed, creating video stream...");

            // --- Set up receiver video stream ---
            var videoStream = new VideoStream(subscribedTrack);
            videoStream.Start();
            var streamUpdateCoroutine = TestCoroutineRunner.Start(videoStream.Update());

            // --- Shared state for latency measurement ---
            var stats = new LatencyStats();
            var sendTimestamps = new long[kTotalPulses];
            var pulseReceived = new bool[kTotalPulses];
            int missedPulses = 0;

            // Subscribe to received video frames (fires on main thread)
            videoStream.FrameReceived += (frame) =>
            {
                int pulseIndex = VideoPulseCodec.Decode(videoStream.VideoBuffer, kVideoStripThreshold);
                if (pulseIndex < 0 || pulseIndex >= kTotalPulses)
                    return;
                if (pulseReceived[pulseIndex])
                    return;
                if (sendTimestamps[pulseIndex] == 0)
                    return;

                long receiveTimeTicks = Stopwatch.GetTimestamp();
                double latencyMs = (receiveTimeTicks - sendTimestamps[pulseIndex])
                    * 1000.0 / Stopwatch.Frequency;

                if (latencyMs > 0 && latencyMs < 5000)
                {
                    pulseReceived[pulseIndex] = true;
                    stats.AddMeasurement(latencyMs);
                    Debug.Log($"  Pulse {pulseIndex}: latency {latencyMs:F2} ms");
                }
            };

            // --- Warm up: send black frames so the encoder stabilizes ---
            Debug.Log($"Warming up encoder for {kVideoWarmupSeconds}s...");
            yield return new WaitForSeconds(kVideoWarmupSeconds);

            // --- Send video pulses ---
            Debug.Log("Starting video pulse transmission...");

            for (int pulsesSent = 0; pulsesSent < kTotalPulses; pulsesSent++)
            {
                int encoded = VideoPulseCodec.Encode(pulsesSent);
                videoSource.SetPulseIndex(encoded);
                sendTimestamps[pulsesSent] = Stopwatch.GetTimestamp();
                Debug.Log($"Sent pulse {pulsesSent + 1}/{kTotalPulses} (pattern: {Convert.ToString(encoded, 2).PadLeft(4, '0')})");

                // Hold pulse so encoder has time to process
                yield return new WaitForSeconds(kVideoPulseDurationSeconds);

                // Reset to black between pulses
                videoSource.SetPulseIndex(VideoPulseCodec.Encode(-1));

                // Wait before next pulse
                yield return new WaitForSeconds(kVideoPulseIntervalSeconds);
            }

            // Wait for last echoes to arrive
            yield return new WaitForSeconds(kVideoEchoTimeoutSeconds);

            // Count missed pulses
            for (int i = 0; i < kTotalPulses; i++)
            {
                if (sendTimestamps[i] > 0 && !pulseReceived[i])
                    missedPulses++;
            }

            // --- Print results ---
            stats.PrintStats("Video Latency Statistics");
            if (missedPulses > 0)
                Debug.Log($"Missed pulses (timeout): {missedPulses}");

            // --- Cleanup ---
            videoStream.Stop();
            videoStream.Dispose();
            TestCoroutineRunner.Stop(streamUpdateCoroutine);
            videoSource.Stop();
            videoSource.Dispose();
            TestCoroutineRunner.Stop(sourceUpdateCoroutine);

            Assert.Greater(stats.Count, 0, "At least one video latency measurement should be recorded");
        }

        // =====================================================================
        // Test 4: A/V Sync Measurement
        // =====================================================================

        // A/V sync configuration
        const float kAVSyncPulseDurationSeconds = 0.5f;
        const float kAVSyncPulseIntervalSeconds = 2f;
        const float kAVSyncEchoTimeoutSeconds = 3f;
        const float kAVSyncWarmupSeconds = 3f;

        [UnityTest, Category("E2E")]
        public IEnumerator AVSync()
        {
            Debug.Log("\n=== A/V Sync Measurement Test ===");
            Debug.Log("Measuring audio-video desync by sending simultaneous tagged pulses");

            // --- Connect two participants ---
            var sender = TestRoomContext.ConnectionOptions.Default;
            sender.Identity = "avsync-sender";
            var receiver = TestRoomContext.ConnectionOptions.Default;
            receiver.Identity = "avsync-receiver";

            using var context = new TestRoomContext(new[] { receiver, sender });
            Debug.Log($"Server used: {context.GetServerUrl()}");
            
            yield return context.ConnectAll();
            if (context.ConnectionError != null)
                Assert.Fail(context.ConnectionError);

            var receiverRoom = context.Rooms[0];
            var senderRoom = context.Rooms[1];

            // --- Wait for sender to be visible to receiver ---
            var participantExpectation = new Expectation(timeoutSeconds: 10f);
            if (receiverRoom.RemoteParticipants.ContainsKey(sender.Identity))
            {
                participantExpectation.Fulfill();
            }
            else
            {
                receiverRoom.ParticipantConnected += (p) =>
                {
                    if (p.Identity == sender.Identity)
                        participantExpectation.Fulfill();
                };
            }
            yield return participantExpectation.Wait();
            if (participantExpectation.Error != null)
                Assert.Fail($"Sender not visible to receiver: {participantExpectation.Error}");

            // --- Publish audio track ---
            var audioSource = new TestAudioSource(channels: kAudioChannels);
            audioSource.Start();
            var audioTrack = LocalAudioTrack.CreateAudioTrack("avsync-audio", audioSource, senderRoom);
            var audioPublish = senderRoom.LocalParticipant.PublishTrack(audioTrack, new TrackPublishOptions());
            yield return audioPublish;
            if (audioPublish.IsError)
                Assert.Fail("Failed to publish audio track");

            // --- Publish video track ---
            var videoSource = new TestVideoSource(kVideoWidth, kVideoHeight);
            videoSource.Start();
            var videoTrack = LocalVideoTrack.CreateVideoTrack("avsync-video", videoSource, senderRoom);
            var videoPublish = senderRoom.LocalParticipant.PublishTrack(videoTrack, new TrackPublishOptions());
            yield return videoPublish;
            if (videoPublish.IsError)
                Assert.Fail("Failed to publish video track");

            var videoSourceCoroutine = TestCoroutineRunner.Start(videoSource.Update());

            Debug.Log("Both tracks published, waiting for subscriptions...");

            // --- Wait for receiver to subscribe to both tracks ---
            RemoteAudioTrack subscribedAudioTrack = null;
            RemoteVideoTrack subscribedVideoTrack = null;
            var bothTracksExpectation = new Expectation(
                predicate: () => subscribedAudioTrack != null && subscribedVideoTrack != null,
                timeoutSeconds: 15f);

            receiverRoom.TrackSubscribed += (track, publication, participant) =>
            {
                if (participant.Identity != sender.Identity) return;
                if (track is RemoteAudioTrack rat) subscribedAudioTrack = rat;
                if (track is RemoteVideoTrack rvt) subscribedVideoTrack = rvt;
            };
            yield return bothTracksExpectation.Wait();
            if (bothTracksExpectation.Error != null)
                Assert.Fail($"Failed to subscribe to both tracks: {bothTracksExpectation.Error}");

            Debug.Log("Both tracks subscribed, setting up receivers...");

            // --- Set up audio receiver ---
            var listenerGO = new GameObject("AVSyncAudioListener");
            listenerGO.AddComponent<AudioListener>();

            var audioReceiverGO = new GameObject("AVSyncAudioReceiver");
            var unityAudioSource = audioReceiverGO.AddComponent<AudioSource>();
            unityAudioSource.spatialBlend = 0f;
            unityAudioSource.volume = 1f;

            var audioStream = new AudioStream(subscribedAudioTrack, unityAudioSource);

            var audioPulseDetector = audioReceiverGO.AddComponent<AudioPulseDetector>();
            audioPulseDetector.TotalPulses = kTotalPulses;
            audioPulseDetector.BaseFrequency = kBaseFrequency;
            audioPulseDetector.FrequencyStep = kFrequencyStep;
            audioPulseDetector.MagnitudeThreshold = kMagnitudeThreshold;

            // --- Set up video receiver ---
            var videoStream = new VideoStream(subscribedVideoTrack);
            videoStream.Start();
            var videoStreamCoroutine = TestCoroutineRunner.Start(videoStream.Update());

            // --- Shared state ---
            var lockObj = new object();
            var sendTimestamps = new long[kTotalPulses];
            var audioReceiveTicks = new long[kTotalPulses];
            var videoReceiveTicks = new long[kTotalPulses];

            // Audio detection (runs on audio thread)
            audioPulseDetector.PulseReceived += (pulseIndex, magnitude) =>
            {
                lock (lockObj)
                {
                    if (pulseIndex < 0 || pulseIndex >= kTotalPulses) return;
                    if (audioReceiveTicks[pulseIndex] != 0) return;
                    if (sendTimestamps[pulseIndex] == 0) return;

                    audioReceiveTicks[pulseIndex] = Stopwatch.GetTimestamp();
                    Debug.Log($"  [Audio] Pulse {pulseIndex} received " +
                              $"(freq: {kBaseFrequency + pulseIndex * kFrequencyStep} Hz)");
                }
            };

            // Video detection (runs on main thread)
            videoStream.FrameReceived += (frame) =>
            {
                int pulseIndex = VideoPulseCodec.Decode(videoStream.VideoBuffer, kVideoStripThreshold);
                if (pulseIndex < 0 || pulseIndex >= kTotalPulses) return;
                if (videoReceiveTicks[pulseIndex] != 0) return;
                if (sendTimestamps[pulseIndex] == 0) return;

                videoReceiveTicks[pulseIndex] = Stopwatch.GetTimestamp();
                Debug.Log($"  [Video] Pulse {pulseIndex} received");
            };

            // --- Audio frame pusher (runs concurrently) ---
            int currentAudioPulseIndex = -1;
            bool audioRunning = true;

            IEnumerator PushAudioFrames()
            {
                var frameDuration = TimeSpan.FromMilliseconds(kAudioFrameDurationMs);
                var nextFrameTime = Stopwatch.GetTimestamp();
                int highEnergyFramesRemaining = 0;
                double currentFrequency = 0;
                int lastPulseIndex = -1;

                while (audioRunning)
                {
                    long now = Stopwatch.GetTimestamp();
                    double waitMs = (nextFrameTime - now) * 1000.0 / Stopwatch.Frequency;
                    if (waitMs > 1.0)
                    {
                        yield return null;
                        continue;
                    }
                    nextFrameTime += (long)(frameDuration.TotalSeconds * Stopwatch.Frequency);

                    int pulseIdx = currentAudioPulseIndex;
                    float[] frameData;

                    if (highEnergyFramesRemaining > 0)
                    {
                        frameData = GenerateToneFrame(kSamplesPerFrame, currentFrequency);
                        highEnergyFramesRemaining--;
                    }
                    else if (pulseIdx >= 0 && pulseIdx != lastPulseIndex)
                    {
                        // New pulse started
                        currentFrequency = kBaseFrequency + pulseIdx * kFrequencyStep;
                        frameData = GenerateToneFrame(kSamplesPerFrame, currentFrequency);
                        highEnergyFramesRemaining = kHighEnergyFramesPerPulse - 1;
                        lastPulseIndex = pulseIdx;
                    }
                    else
                    {
                        frameData = GenerateSilentFrame(kSamplesPerFrame);
                    }

                    audioSource.PushFrame(frameData, kAudioChannels, kAudioSampleRate);
                }
            }

            var audioFrameCoroutine = TestCoroutineRunner.Start(PushAudioFrames());

            // --- Warm up ---
            Debug.Log($"Warming up for {kAVSyncWarmupSeconds}s...");
            yield return new WaitForSeconds(kAVSyncWarmupSeconds);

            // --- Send simultaneous A/V pulses ---
            Debug.Log("Starting A/V pulse transmission...");

            for (int pulsesSent = 0; pulsesSent < kTotalPulses; pulsesSent++)
            {
                // Start both audio and video pulse simultaneously
                int videoEncoded = VideoPulseCodec.Encode(pulsesSent);
                videoSource.SetPulseIndex(videoEncoded);
                lock (lockObj)
                {
                    sendTimestamps[pulsesSent] = Stopwatch.GetTimestamp();
                }
                currentAudioPulseIndex = pulsesSent;

                Debug.Log($"Sent A/V pulse {pulsesSent + 1}/{kTotalPulses}");

                // Hold pulse
                yield return new WaitForSeconds(kAVSyncPulseDurationSeconds);

                // Reset both to idle
                videoSource.SetPulseIndex(VideoPulseCodec.Encode(-1));
                currentAudioPulseIndex = -1;

                // Wait before next pulse
                yield return new WaitForSeconds(kAVSyncPulseIntervalSeconds);
            }

            // Wait for last echoes
            yield return new WaitForSeconds(kAVSyncEchoTimeoutSeconds);

            // Stop audio pusher
            audioRunning = false;
            yield return null;

            // --- Compute results ---
            var audioStats = new LatencyStats();
            var videoStats = new LatencyStats();
            var desyncStats = new LatencyStats();
            int audioMissed = 0, videoMissed = 0, bothReceived = 0;

            lock (lockObj)
            {
                for (int i = 0; i < kTotalPulses; i++)
                {
                    if (sendTimestamps[i] == 0) continue;

                    bool hasAudio = audioReceiveTicks[i] != 0;
                    bool hasVideo = videoReceiveTicks[i] != 0;

                    if (!hasAudio) audioMissed++;
                    if (!hasVideo) videoMissed++;

                    double audioLatencyMs = hasAudio
                        ? (audioReceiveTicks[i] - sendTimestamps[i]) * 1000.0 / Stopwatch.Frequency
                        : -1;
                    double videoLatencyMs = hasVideo
                        ? (videoReceiveTicks[i] - sendTimestamps[i]) * 1000.0 / Stopwatch.Frequency
                        : -1;

                    if (hasAudio && audioLatencyMs > 0 && audioLatencyMs < 5000)
                        audioStats.AddMeasurement(audioLatencyMs);
                    if (hasVideo && videoLatencyMs > 0 && videoLatencyMs < 5000)
                        videoStats.AddMeasurement(videoLatencyMs);

                    if (hasAudio && hasVideo && audioLatencyMs > 0 && videoLatencyMs > 0)
                    {
                        double desyncMs = videoLatencyMs - audioLatencyMs;
                        desyncStats.AddMeasurement(desyncMs);
                        bothReceived++;
                        Debug.Log($"  Pulse {i}: audio={audioLatencyMs:F2}ms video={videoLatencyMs:F2}ms desync={desyncMs:F2}ms");
                    }
                }
            }

            // --- Print results ---
            audioStats.PrintStats("A/V Sync - Audio Latency");
            videoStats.PrintStats("A/V Sync - Video Latency");
            desyncStats.PrintStats("A/V Sync - Desync (positive = video lags audio)");

            Debug.Log($"Pulses with both A+V received: {bothReceived}/{kTotalPulses}");
            if (audioMissed > 0) Debug.Log($"Audio missed: {audioMissed}");
            if (videoMissed > 0) Debug.Log($"Video missed: {videoMissed}");

            // --- Cleanup ---
            TestCoroutineRunner.Stop(audioFrameCoroutine);
            audioStream.Dispose();
            UnityEngine.Object.Destroy(audioReceiverGO);
            UnityEngine.Object.Destroy(listenerGO);
            audioSource.Stop();
            audioSource.Dispose();

            videoStream.Stop();
            videoStream.Dispose();
            TestCoroutineRunner.Stop(videoStreamCoroutine);
            videoSource.Stop();
            videoSource.Dispose();
            TestCoroutineRunner.Stop(videoSourceCoroutine);

            Assert.Greater(bothReceived, 0, "At least one pulse should be received by both audio and video");
        }

        // =====================================================================
        // Audio Generation Helpers
        // =====================================================================

        static float[] GenerateToneFrame(int samplesPerChannel, double frequency)
        {
            var data = new float[samplesPerChannel * kAudioChannels];
            const double amplitude = 0.9;

            for (int i = 0; i < samplesPerChannel; i++)
            {
                double t = (double)i / kAudioSampleRate;
                float sample = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * t));
                for (int ch = 0; ch < kAudioChannels; ch++)
                {
                    data[i * kAudioChannels + ch] = sample;
                }
            }
            return data;
        }

        /// <summary>
        /// Goertzel algorithm for S16 PCM data accessed via IntPtr.
        /// Mirrors AudioPulseDetector.Goertzel but reads short samples from native memory.
        /// </summary>
        static unsafe double GoertzelS16(IntPtr data, int channels, int N, int sampleRate, double freq)
        {
            short* samples = (short*)data;
            double k = 0.5 + (double)N * freq / sampleRate;
            double w = 2.0 * Math.PI * k / N;
            double coeff = 2.0 * Math.Cos(w);
            double s0 = 0, s1 = 0, s2 = 0;

            for (int i = 0; i < N; i++)
            {
                double sample = samples[i * channels] / 32768.0;
                s0 = sample + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }

            double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            return Math.Sqrt(Math.Abs(power)) / N;
        }

        static float[] GenerateSilentFrame(int samplesPerChannel)
        {
            return new float[samplesPerChannel * kAudioChannels];
        }

        // =====================================================================
        // Latency Stats Helper
        // =====================================================================

        class LatencyStats
        {
            readonly List<double> _measurements = new();
            readonly object _lock = new();

            public int Count
            {
                get { lock (_lock) return _measurements.Count; }
            }

            public void AddMeasurement(double ms)
            {
                lock (_lock) _measurements.Add(ms);
            }

            public void PrintStats(string title)
            {
                lock (_lock)
                {
                    Debug.Log($"\n--- {title} ---");
                    if (_measurements.Count == 0)
                    {
                        Debug.Log("  No measurements recorded");
                        return;
                    }

                    double min = double.MaxValue, max = double.MinValue, sum = 0;
                    foreach (var m in _measurements)
                    {
                        if (m < min) min = m;
                        if (m > max) max = m;
                        sum += m;
                    }
                    double avg = sum / _measurements.Count;

                    double sumSqDiff = 0;
                    foreach (var m in _measurements)
                    {
                        double diff = m - avg;
                        sumSqDiff += diff * diff;
                    }
                    double stddev = Math.Sqrt(sumSqDiff / _measurements.Count);

                    Debug.Log($"  Count:  {_measurements.Count}");
                    Debug.Log($"  Min:    {min:F2} ms");
                    Debug.Log($"  Max:    {max:F2} ms");
                    Debug.Log($"  Avg:    {avg:F2} ms");
                    Debug.Log($"  StdDev: {stddev:F2} ms");
                }
            }
        }
    }
}
