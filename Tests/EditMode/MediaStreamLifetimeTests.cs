using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace LiveKit.EditModeTests
{
    public class MediaStreamLifetimeTests
    {
        private static readonly string[] AudioStreamPaths =
        {
            "Runtime/Scripts/AudioStream.cs",
            "Assets/client-sdk-unity/Runtime/Scripts/AudioStream.cs",
        };

        private static readonly string[] VideoStreamPaths =
        {
            "Runtime/Scripts/VideoStream.cs",
            "Assets/client-sdk-unity/Runtime/Scripts/VideoStream.cs",
        };

        private static readonly string[] AudioResamplerPaths =
        {
            "Runtime/Scripts/Internal/AudioResampler.cs",
            "Assets/client-sdk-unity/Runtime/Scripts/Internal/AudioResampler.cs",
        };

        private static string ReadSource(params string[] candidates)
        {
            foreach (var root in SearchRoots())
            {
                foreach (var candidate in candidates)
                {
                    var combined = Path.GetFullPath(Path.Combine(root, candidate));
                    if (File.Exists(combined))
                    {
                        return File.ReadAllText(combined);
                    }
                }
            }

            foreach (var root in SearchRoots())
            {
                foreach (var candidate in candidates)
                {
                    var fileName = Path.GetFileName(candidate);
                    if (string.IsNullOrEmpty(fileName) || !Directory.Exists(root))
                    {
                        continue;
                    }

                    try
                    {
                        foreach (var match in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
                        {
                            // Keep the search specific to the intended suffix so a duplicate file
                            // name elsewhere in the repo does not satisfy the test accidentally.
                            var normalizedMatch = match.Replace('\\', '/');
                            var normalizedCandidate = candidate.Replace('\\', '/');
                            if (normalizedMatch.EndsWith(normalizedCandidate))
                            {
                                return File.ReadAllText(match);
                            }
                        }
                    }
                    catch (IOException)
                    {
                        // Best-effort lookup for CI layout differences.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Best-effort lookup for CI layout differences.
                    }
                }
            }

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }

            Assert.Fail($"Could not find source file. Tried: {string.Join(", ", candidates)}");
            return string.Empty;
        }

        private static string[] SearchRoots()
        {
            var roots = new System.Collections.Generic.List<string>();

            void AddWithParents(string path)
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                var fullPath = Path.GetFullPath(path);
                var dir = new DirectoryInfo(fullPath);
                while (dir != null)
                {
                    if (!roots.Contains(dir.FullName))
                    {
                        roots.Add(dir.FullName);
                    }
                    dir = dir.Parent;
                }
            }

            AddWithParents(Directory.GetCurrentDirectory());
            AddWithParents(Application.dataPath);

            return roots.ToArray();
        }

        [Test]
        public void AudioStream_Dispose_UnsubscribesAndReleasesOwnedResources()
        {
            var source = ReadSource(AudioStreamPaths);

            StringAssert.Contains("FfiClient.Instance.AudioStreamEventReceived -= OnAudioStreamEvent;", source);
            StringAssert.Contains("_probe.AudioRead -= OnAudioRead;", source);
            StringAssert.Contains("_buffer?.Dispose();", source);
            StringAssert.Contains("_resampler?.Dispose();", source);
            StringAssert.Contains("Handle.Dispose();", source);
        }

        [Test]
        public void AudioStream_AudioFrames_AreDisposedAfterProcessing()
        {
            var source = ReadSource(AudioStreamPaths);

            // Both the inbound native frame and the remixed output frame should be scoped so their
            // handles are released after each callback rather than accumulating over time.
            StringAssert.Contains("using var frame = new AudioFrame(e.FrameReceived.Frame);", source);
            StringAssert.Contains("using var uFrame = _resampler.RemixAndResample(frame, _numChannels, _sampleRate);", source);
        }

        [Test]
        public void AudioResampler_IsDisposable_AndReleasesNativeHandle()
        {
            var source = ReadSource(AudioResamplerPaths);

            StringAssert.Contains("public sealed class AudioResampler : IDisposable", source);
            StringAssert.Contains("_handle.Dispose();", source);
        }

        [Test]
        public void VideoStream_Dispose_UnsubscribesAndReleasesOwnedResources()
        {
            var source = ReadSource(VideoStreamPaths);

            StringAssert.Contains("FfiClient.Instance.VideoStreamEventReceived -= OnVideoStreamEvent;", source);
            StringAssert.Contains("VideoBuffer?.Dispose();", source);
            StringAssert.Contains("_pendingBuffer?.Dispose();", source);
            StringAssert.Contains("Handle.Dispose();", source);
        }

        [Test]
        public void VideoStream_UsesLatestFrameWinsCoalescing()
        {
            var source = ReadSource(VideoStreamPaths);

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
