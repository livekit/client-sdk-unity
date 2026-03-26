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

        // Energy detection
        const double kHighEnergyThreshold = 0.3;
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
        // Test 2: Audio Latency Measurement using Energy Detection
        // =====================================================================

        [UnityTest, Category("E2E")]
        public IEnumerator AudioLatency()
        {
            Debug.Log("\n=== Audio Latency Measurement Test ===");
            Debug.Log("Using energy detection to measure audio round-trip latency");

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

            // --- Set up receiver audio stream + energy detector ---
            var listenerGO = new GameObject("LatencyTestAudioListener");
            listenerGO.AddComponent<AudioListener>(); // Required for OnAudioFilterRead to fire

            var receiverGO = new GameObject("LatencyTestReceiver");
            var unityAudioSource = receiverGO.AddComponent<AudioSource>();
            unityAudioSource.spatialBlend = 0f; // 2D audio
            unityAudioSource.volume = 1f;

            // AudioStream constructor adds AudioProbe and starts playback
            var audioStream = new AudioStream(subscribedTrack, unityAudioSource);

            // Add EnergyDetector AFTER AudioStream so OnAudioFilterRead order is correct
            var energyDetector = receiverGO.AddComponent<EnergyDetector>();

            // --- Shared state for latency measurement ---
            var stats = new LatencyStats();
            var lockObj = new object();
            long lastHighEnergySendTimeTicks = 0;
            bool waitingForEcho = false;
            int missedPulses = 0;

            // Energy detector callback (runs on audio thread)
            energyDetector.EnergyDetected += (energy) =>
            {
                lock (lockObj)
                {
                    if (!waitingForEcho || energy <= kHighEnergyThreshold)
                        return;

                    long receiveTimeTicks = Stopwatch.GetTimestamp();
                    long sendTimeTicks = lastHighEnergySendTimeTicks;

                    if (sendTimeTicks > 0)
                    {
                        double latencyMs = (receiveTimeTicks - sendTimeTicks)
                            * 1000.0 / Stopwatch.Frequency;

                        if (latencyMs > 0 && latencyMs < 5000)
                        {
                            stats.AddMeasurement(latencyMs);
                            Debug.Log($"Audio latency: {latencyMs:F2} ms (energy: {energy:F3})");
                        }
                        waitingForEcho = false;
                    }
                }
            };

            // --- Send audio frames in real-time ---
            Debug.Log("Starting audio pulse transmission...");

            int frameCount = 0;
            int pulsesSent = 0;
            int highEnergyFramesRemaining = 0;
            var frameDuration = TimeSpan.FromMilliseconds(kAudioFrameDurationMs);
            var nextFrameTime = Stopwatch.GetTimestamp();

            while (pulsesSent < kTotalPulses)
            {
                // Wait until it's time to send the next frame
                long now = Stopwatch.GetTimestamp();
                double waitMs = (nextFrameTime - now) * 1000.0 / Stopwatch.Frequency;
                if (waitMs > 1.0)
                {
                    yield return null; // yield to Unity, come back next frame
                    continue;
                }
                nextFrameTime += (long)(frameDuration.TotalSeconds * Stopwatch.Frequency);

                float[] frameData;

                // Check for echo timeout
                lock (lockObj)
                {
                    if (waitingForEcho && lastHighEnergySendTimeTicks > 0)
                    {
                        double elapsedMs = (Stopwatch.GetTimestamp() - lastHighEnergySendTimeTicks)
                            * 1000.0 / Stopwatch.Frequency;
                        if (elapsedMs > kEchoTimeoutSeconds * 1000)
                        {
                            Debug.Log($"  Echo timeout for pulse {pulsesSent}, moving on...");
                            waitingForEcho = false;
                            missedPulses++;
                        }
                    }
                }

                if (highEnergyFramesRemaining > 0)
                {
                    // Continue sending high-energy frames for current pulse
                    frameData = GenerateHighEnergyFrame(kSamplesPerFrame);
                    highEnergyFramesRemaining--;
                }
                else
                {
                    bool shouldPulse;
                    lock (lockObj) { shouldPulse = !waitingForEcho; }

                    if (frameCount % kFramesBetweenPulses == 0 && shouldPulse)
                    {
                        // Start a new pulse
                        frameData = GenerateHighEnergyFrame(kSamplesPerFrame);
                        highEnergyFramesRemaining = kHighEnergyFramesPerPulse - 1;

                        lock (lockObj)
                        {
                            lastHighEnergySendTimeTicks = Stopwatch.GetTimestamp();
                            waitingForEcho = true;
                        }
                        pulsesSent++;
                        Debug.Log($"Sent pulse {pulsesSent}/{kTotalPulses} ({kHighEnergyFramesPerPulse} frames)");
                    }
                    else
                    {
                        frameData = GenerateSilentFrame(kSamplesPerFrame);
                    }
                }

                audioSource.PushFrame(frameData, kAudioChannels, kAudioSampleRate);
                frameCount++;
            }

            // Wait for last echo
            yield return new WaitForSeconds(kEchoTimeoutSeconds);

            // --- Print results ---
            stats.PrintStats("Audio Latency Statistics");
            if (missedPulses > 0)
                Debug.Log($"Missed pulses (timeout): {missedPulses}");

            // --- Cleanup ---
            audioStream.Dispose();
            UnityEngine.Object.Destroy(receiverGO);
            UnityEngine.Object.Destroy(listenerGO);
            audioSource.Stop();
            audioSource.Dispose();

            Assert.Greater(stats.Count, 0, "At least one audio latency measurement should be recorded");
        }

        // =====================================================================
        // Audio Generation Helpers
        // =====================================================================

        static float[] GenerateHighEnergyFrame(int samplesPerChannel)
        {
            var data = new float[samplesPerChannel * kAudioChannels];
            const double frequency = 1000.0; // 1kHz sine wave
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
