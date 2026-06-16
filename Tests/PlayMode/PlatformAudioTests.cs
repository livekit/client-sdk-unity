using System;
using System.Collections;
using System.Collections.Generic;
using LiveKit.PlayModeTests.Utils;
using LiveKit.Proto;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// ADM-backed unit tests for PlatformAudio / PlatformAudioSource. These exercise the
    /// FFI but do NOT need a LiveKit server, so they are not tagged Category("E2E").
    /// Each test skips cleanly (Assert.Ignore) when no platform ADM is available.
    /// </summary>
    public class PlatformAudioTests
    {
        [UnityTest]
        public IEnumerator CreateSourceAndTrack_WhenAvailable()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            using var source = new PlatformAudioSource(platformAudio);
            Assert.AreNotEqual(0, (long)source.Handle.DangerousGetHandle(), "source handle should be non-zero");

            // CreateAudioTrack only uses room?.LocalParticipant, so a null room is fine for
            // constructing the track from the source handle.
            var track = LocalAudioTrack.CreateAudioTrack("platform-mic", source, null);
            Assert.IsNotNull(track);
            Assert.AreEqual("platform-mic", track.Name);
            Assert.AreEqual(TrackKind.KindAudio, track.Kind);

            yield break;
        }

        [UnityTest]
        public IEnumerator CreateSource_WithCustomOptions()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            var options = new AudioProcessingOptions
            {
                EchoCancellation = false,
                NoiseSuppression = false,
                AutoGainControl = false,
                PreferHardware = true
            };

            using var source = new PlatformAudioSource(platformAudio, options);
            Assert.AreNotEqual(0, (long)source.Handle.DangerousGetHandle(), "source handle should be non-zero");

            yield break;
        }

        [UnityTest]
        public IEnumerator EnumerateDevices_AndSelectByGuid_DoesNotThrow()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            // Enumeration must succeed even on headless runners (it may return empty lists).
            List<AudioDevice> recording = null;
            List<AudioDevice> playout = null;
            Assert.DoesNotThrow(() => (recording, playout) = platformAudio.GetDevices());

            // Selecting a real device by its stable GUID must not throw. Headless runners
            // usually report no devices, so guard the assertion behind availability.
            var selectedAny = false;
            foreach (var device in recording)
            {
                if (!string.IsNullOrEmpty(device.Guid))
                {
                    Assert.DoesNotThrow(() => platformAudio.SetRecordingDevice(device.Guid));
                    selectedAny = true;
                    break;
                }
            }
            foreach (var device in playout)
            {
                if (!string.IsNullOrEmpty(device.Guid))
                {
                    Assert.DoesNotThrow(() => platformAudio.SetPlayoutDevice(device.Guid));
                    selectedAny = true;
                    break;
                }
            }

            if (!selectedAny)
                Assert.Ignore("No audio devices with stable GUIDs available to select");

            yield break;
        }

        [UnityTest]
        public IEnumerator SetRecordingDeviceByIndex_OutOfRange_Throws()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            // The uint convenience overload validates the index against the enumerated list
            // before calling the GUID overload. 9999 is out of range regardless of device count.
            Assert.Throws<InvalidOperationException>(() => platformAudio.SetRecordingDevice((uint)9999));

            yield break;
        }

        [UnityTest]
        public IEnumerator StartThenStopRecording_DoesNotThrow()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            // StartRecording is a coroutine (it awaits the Android permission dialog on-device).
            // In the editor there is no PLATFORM_ANDROID branch, so it sends the FFI request
            // synchronously. A headless ADM may legitimately fail to start recording; treat that
            // as "ADM can't record here" and skip rather than fail.
            var start = platformAudio.StartRecording();
            while (true)
            {
                bool moved;
                try
                {
                    moved = start.MoveNext();
                }
                catch (InvalidOperationException e)
                {
                    Assert.Ignore($"Recording unavailable in this environment: {e.Message}");
                    yield break;
                }

                if (!moved) break;
                yield return start.Current;
            }

            Assert.DoesNotThrow(() => platformAudio.StopRecording());
        }
    }
}
