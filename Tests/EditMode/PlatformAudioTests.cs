using System;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    /// <summary>
    /// Pure-managed unit tests for the PlatformAudio public surface. These never touch
    /// the FFI, the platform Audio Device Module, or a server, so they always run
    /// (including on headless CI runners with no audio subsystem).
    /// </summary>
    public class PlatformAudioTests
    {
        [Test]
        public void AudioProcessingOptions_Default_EnablesProcessingAndHardware()
        {
            var options = AudioProcessingOptions.Default;

            Assert.IsTrue(options.EchoCancellation, "AEC should be enabled by default");
            Assert.IsTrue(options.NoiseSuppression, "NS should be enabled by default");
            Assert.IsTrue(options.AutoGainControl, "AGC should be enabled by default");
            // Unlike the C++ defaults (prefer_hardware == false), the Unity default prefers
            // hardware processing (e.g. iOS VPIO) for lower latency.
            Assert.IsTrue(options.PreferHardware, "Unity default prefers hardware processing");
        }

        [Test]
        public void AudioDevice_StoresIndexNameGuid()
        {
            var device = new AudioDevice
            {
                Index = 1,
                Name = "Microphone",
                Guid = "device-guid"
            };

            Assert.AreEqual(1u, device.Index);
            Assert.AreEqual("Microphone", device.Name);
            Assert.AreEqual("device-guid", device.Guid);
        }

        [Test]
        public void PlatformAudioSource_NullPlatformAudio_Throws()
        {
            // The null guard runs before any FFI call, so this is safe without an ADM.
            Assert.Throws<ArgumentNullException>(() => new PlatformAudioSource(null));
        }
    }
}
