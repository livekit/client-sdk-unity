using System.IO;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class MediaStreamLifetimeTests
    {
        private const string AudioStreamPath = "Runtime/Scripts/AudioStream.cs";
        private const string VideoStreamPath = "Runtime/Scripts/VideoStream.cs";
        private const string AudioResamplerPath = "Runtime/Scripts/Internal/AudioResampler.cs";

        [Test]
        public void AudioStream_Dispose_UnsubscribesAndReleasesOwnedResources()
        {
            var source = File.ReadAllText(AudioStreamPath);

            StringAssert.Contains("FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;", source);
            StringAssert.Contains("_probe.AudioRead -= OnAudioRead;", source);
            StringAssert.Contains("_buffer?.Dispose();", source);
            StringAssert.Contains("_resampler?.Dispose();", source);
            StringAssert.Contains("Handle.Dispose();", source);
        }

        [Test]
        public void AudioStream_AudioFrames_AreDisposedAfterProcessing()
        {
            var source = File.ReadAllText(AudioStreamPath);

            // Both the inbound native frame and the remixed output frame should be scoped so their
            // handles are released after each callback rather than accumulating over time.
            StringAssert.Contains("using var frame = new AudioFrame(e.FrameReceived.Frame);", source);
            StringAssert.Contains("using var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);", source);
        }

        [Test]
        public void AudioResampler_IsDisposable_AndReleasesNativeHandle()
        {
            var source = File.ReadAllText(AudioResamplerPath);

            StringAssert.Contains("public sealed class AudioResampler : IDisposable", source);
            StringAssert.Contains("_handle.Dispose();", source);
        }

        [Test]
        public void VideoStream_Dispose_UnsubscribesAndReleasesOwnedResources()
        {
            var source = File.ReadAllText(VideoStreamPath);

            StringAssert.Contains("FfiClient.Instance.VideoStreamEventReceived -= OnVideoStreamEvent;", source);
            StringAssert.Contains("VideoBuffer?.Dispose();", source);
            StringAssert.Contains("_pendingBuffer?.Dispose();", source);
            StringAssert.Contains("Handle.Dispose();", source);
        }

        [Test]
        public void VideoStream_UsesLatestFrameWinsCoalescing()
        {
            var source = File.ReadAllText(VideoStreamPath);

            // The intake path should maintain a dedicated pending slot and replace/drop superseded
            // frames so Unity uploads at most the latest frame per tick.
            StringAssert.Contains("private VideoFrameBuffer _pendingBuffer;", source);
            StringAssert.Contains("_pendingBuffer?.Dispose();", source);
            StringAssert.Contains("_pendingBuffer = buffer;", source);
            StringAssert.Contains("nextBuffer = _pendingBuffer;", source);
            StringAssert.Contains("VideoBuffer = nextBuffer;", source);
        }
    }
}
