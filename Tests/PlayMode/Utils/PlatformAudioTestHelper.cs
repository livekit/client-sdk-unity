using System;
using NUnit.Framework;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// Shared helpers for PlatformAudio tests. PlatformAudio requires a working platform
    /// Audio Device Module; constructing it throws on environments without one (e.g. a
    /// headless CI runner with no audio subsystem). Mirrors the C++ suite's GTEST_SKIP.
    /// </summary>
    internal static class PlatformAudioTestHelper
    {
        /// <summary>
        /// Creates a PlatformAudio, or skips the calling test (Assert.Ignore) when the
        /// platform ADM is unavailable. Never returns null on the happy path.
        /// </summary>
        internal static PlatformAudio TryCreateOrIgnore()
        {
            try
            {
                return new PlatformAudio();
            }
            catch (InvalidOperationException e)
            {
                Assert.Ignore($"PlatformAudio unavailable: {e.Message}");
                return null; // unreachable: Assert.Ignore throws
            }
        }
    }
}
