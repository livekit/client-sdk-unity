using System;
using System.Collections;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// Tests for the Unity-audio processing stage. They need the FFI for the module and, for the
    /// echo test, Unity's audio thread. No LiveKit server.
    /// </summary>
    class AudioProcessingTests
    {
        [Test]
        public void AudioProcessingModule_AcceptsTenMillisecondChunks()
        {
            using var apm = new AudioProcessingModule(
                echoCancellerEnabled: true,
                gainControllerEnabled: true,
                highPassFilterEnabled: true,
                noiseSuppressionEnabled: true);

            const int rate = 48000;
            const int channels = 2;
            var chunk = new short[AudioProcessingModule.FrameSizeFor(rate) * channels];
            var pin = GCHandle.Alloc(chunk, GCHandleType.Pinned);
            try
            {
                var ptr = pin.AddrOfPinnedObject();
                var bytes = chunk.Length * sizeof(short);
                Assert.IsNull(apm.ProcessReverseStream(ptr, bytes, rate, channels));
                Assert.IsNull(apm.ProcessStream(ptr, bytes, rate, channels));
                Assert.IsNull(apm.SetStreamDelayMs(80));
            }
            finally
            {
                pin.Free();
            }
        }

        [UnityTest]
        public IEnumerator RtcAudioSource_WithProcessing_CapturesInTenMillisecondChunks()
        {
            using var source = new PushAudioSource(AudioProcessingOptions.Default);
            Assert.IsTrue(source.AudioProcessingEnabled, "module creation failed");

            var rate = (int)source._expectedSampleRate;
            var channels = (int)source._expectedChannels;
            if (!AudioProcessingModule.IsSupportedApiRate(rate))
                Assert.Ignore($"output rate {rate} has no whole-sample 10 ms chunk");

            source.Start();

            const int blockFrames = 1024;
            const int blocks = 10;
            var block = new float[blockFrames * channels];
            for (int i = 0; i < block.Length; i++)
                block[i] = 0.1f * Mathf.Sin(i * 0.05f);
            for (int i = 0; i < blocks; i++)
                source.Push(block, channels, rate);

            // Let the capture callbacks return before disposing.
            yield return new WaitForSeconds(0.2f);

            var stats = source.AudioProcessingStats;
            var expectedChunks = blockFrames * blocks / AudioProcessingModule.FrameSizeFor(rate);
            Assert.IsTrue(stats.Active, stats.ToString());
            Assert.AreEqual(0, stats.FailedChunks, stats.LastError);
            Assert.That(stats.CaptureChunks, Is.InRange(expectedChunks - 1, expectedChunks), stats.ToString());
            Assert.AreEqual(0, stats.DroppedCaptureSamples, stats.ToString());

            source.Stop();
        }

        /// <summary>
        /// End-to-end check of the canceller without hardware: the listener hears a noise source,
        /// and a second source plays the same noise 120 ms later, probed as "microphone" and then
        /// cleared so the mix contains only the far end. The capture is therefore a pure delayed
        /// echo of the playout reference, which AEC3 must learn to remove.
        /// </summary>
        [UnityTest]
        public IEnumerator AudioProcessor_CancelsDelayedEchoOfPlayout()
        {
            var rate = AudioSettings.outputSampleRate;
            if (!AudioProcessingModule.IsSupportedApiRate(rate))
                Assert.Ignore($"output rate {rate} has no whole-sample 10 ms chunk");

            var listenerGo = new GameObject("AecTestListener");
            listenerGo.AddComponent<AudioListener>();

            var clip = NoiseClip(rate, seconds: 2f, seed: 1234, amplitude: 0.3f);

            var farGo = new GameObject("AecTestFarEnd");
            var far = farGo.AddComponent<AudioSource>();
            far.clip = clip;
            far.loop = true;

            var nearGo = new GameObject("AecTestNearEnd");
            var near = nearGo.AddComponent<AudioSource>();
            near.clip = clip;
            near.loop = true;
            var probe = nearGo.AddComponent<AudioProbe>();
            probe.ClearAfterInvocation();

            var meter = new EchoMeter();
            var processor = new AudioProcessor(
                new AudioProcessingOptions { EchoCancellation = true, HighPassFilter = true },
                meter.OnProcessed);
            probe.AudioRead += (data, channels, sampleRate) =>
            {
                meter.OnRaw(data);
                processor.TryProcessCapture(data, channels, sampleRate);
            };
            processor.Start();

            var startTime = AudioSettings.dspTime + 0.2;
            far.PlayScheduled(startTime);
            near.PlayScheduled(startTime + 0.12);

            AudioProcessingStats stats;
            float rawRms, processedRms;
            try
            {
                yield return new WaitForSeconds(1f);
                if (meter.RawBlocks == 0)
                    Assert.Ignore("Unity's audio thread delivered no capture callbacks (no audio device?)");
                Assert.IsTrue(PlayoutReference.IsAttached, "reference not attached to the listener");

                // Convergence time, then a clean measurement window.
                yield return new WaitForSeconds(3f);
                meter.ResetWindow();
                yield return new WaitForSeconds(1.5f);

                (rawRms, processedRms) = meter.Window();
                stats = processor.GetStats();
            }
            finally
            {
                processor.Dispose();
                UnityEngine.Object.Destroy(farGo);
                UnityEngine.Object.Destroy(nearGo);
                UnityEngine.Object.Destroy(listenerGo);
            }

            Assert.Greater(rawRms, 0.01f, "near-end source produced no signal");
            Assert.AreEqual(0, stats.FailedChunks, stats.ToString());
            Assert.Greater(stats.ReferenceChunks, 0, stats.ToString());

            var attenuationDb = 20f * Mathf.Log10(rawRms / Mathf.Max(processedRms, 1e-6f));
            Debug.Log($"AEC3 attenuated the synthetic echo by {attenuationDb:F1} dB ({stats})");
            Assert.GreaterOrEqual(attenuationDb, 6f, $"AEC3 attenuated the echo by only {attenuationDb:F1} dB; {stats}");
        }

        private static AudioClip NoiseClip(int sampleRate, float seconds, int seed, float amplitude)
        {
            var samples = (int)(sampleRate * seconds);
            var data = new float[samples];
            var random = new System.Random(seed);
            for (int i = 0; i < samples; i++)
                data[i] = amplitude * (float)(random.NextDouble() * 2.0 - 1.0);

            var clip = AudioClip.Create("AecTestNoise", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private sealed class PushAudioSource : RtcAudioSource
        {
            public override event Action<float[], int, int> AudioRead;

            public PushAudioSource(AudioProcessingOptions options)
                : base(RtcAudioSourceType.AudioSourceMicrophone, options) { }

            public void Push(float[] data, int channels, int sampleRate) => AudioRead?.Invoke(data, channels, sampleRate);
        }

        // Accumulates energy of the raw near end and of the processed output. Both callbacks run on
        // the Unity audio thread; the sink owns and disposes the frames it is handed.
        private sealed class EchoMeter
        {
            private readonly object _lock = new object();
            private double _rawSum;
            private long _rawCount;
            private double _processedSum;
            private long _processedCount;
            private int _rawBlocks;

            public int RawBlocks { get { lock (_lock) return _rawBlocks; } }

            public void OnRaw(float[] data)
            {
                double sum = 0;
                for (int i = 0; i < data.Length; i++) sum += data[i] * data[i];
                lock (_lock)
                {
                    _rawSum += sum;
                    _rawCount += data.Length;
                    _rawBlocks++;
                }
            }

            public void OnProcessed(NativeArray<short> frame, int channels, int sampleRate)
            {
                double sum = 0;
                var length = frame.Length;
                for (int i = 0; i < length; i++)
                {
                    var v = frame[i] / 32768.0;
                    sum += v * v;
                }
                frame.Dispose();
                lock (_lock)
                {
                    _processedSum += sum;
                    _processedCount += length;
                }
            }

            public void ResetWindow()
            {
                lock (_lock)
                {
                    _rawSum = 0;
                    _rawCount = 0;
                    _processedSum = 0;
                    _processedCount = 0;
                }
            }

            public (float raw, float processed) Window()
            {
                lock (_lock) return (Rms(_rawSum, _rawCount), Rms(_processedSum, _processedCount));
            }

            private static float Rms(double sum, long count) => count == 0 ? 0f : (float)Math.Sqrt(sum / count);
        }
    }
}
