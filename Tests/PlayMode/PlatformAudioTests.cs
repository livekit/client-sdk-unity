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
        public IEnumerator PlayoutPreference_DefaultsAndRoundtrips()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            // Documented default ranking.
            CollectionAssert.AreEqual(
                new[]
                {
                    AudioDeviceKind.Bluetooth,
                    AudioDeviceKind.WiredHeadset,
                    AudioDeviceKind.Speaker,
                    AudioDeviceKind.Earpiece,
                },
                platformAudio.PlayoutPreference);

            // Set/get roundtrip preserves order and content.
            var ranked = new[] { AudioDeviceKind.Usb, AudioDeviceKind.Speaker, AudioDeviceKind.Bluetooth };
            platformAudio.PlayoutPreference = ranked;
            CollectionAssert.AreEqual(ranked, platformAudio.PlayoutPreference);

            yield break;
        }

        [UnityTest]
        public IEnumerator PlayoutPreference_RejectsInvalidLists()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            Assert.Throws<ArgumentNullException>(() => platformAudio.PlayoutPreference = null);
            Assert.Throws<ArgumentException>(() =>
                platformAudio.PlayoutPreference = new[] { AudioDeviceKind.Unknown });
            Assert.Throws<ArgumentException>(() =>
                platformAudio.PlayoutPreference = new[] { AudioDeviceKind.Speaker, AudioDeviceKind.Speaker });

            // A rejected assignment leaves the stored preference untouched.
            CollectionAssert.AreEqual(
                new[]
                {
                    AudioDeviceKind.Bluetooth,
                    AudioDeviceKind.WiredHeadset,
                    AudioDeviceKind.Speaker,
                    AudioDeviceKind.Earpiece,
                },
                platformAudio.PlayoutPreference);

            yield break;
        }

        [UnityTest]
        public IEnumerator SetPlayoutDevice_UnknownGuid()
        {
            using var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

#if UNITY_IOS && !UNITY_EDITOR
            // iOS ignores the call (with a warning): the OS owns route selection.
            Assert.DoesNotThrow(() => platformAudio.SetPlayoutDevice("no-such-guid"));
#elif UNITY_ANDROID && !UNITY_EDITOR
            // Android 12+ validates against the communication-device list; older Android has
            // no routing backend and ignores the call.
            var sdkInt = new UnityEngine.AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
            if (sdkInt >= 31)
                Assert.Throws<InvalidOperationException>(() => platformAudio.SetPlayoutDevice("no-such-guid"));
            else
                Assert.DoesNotThrow(() => platformAudio.SetPlayoutDevice("no-such-guid"));
#else
            // Desktop: the FFI validates the id against the ADM's device list.
            Assert.Throws<InvalidOperationException>(() => platformAudio.SetPlayoutDevice("no-such-guid"));
#endif

            // Clearing is always safe, whether or not an override exists.
            Assert.DoesNotThrow(() => platformAudio.ClearPlayoutDeviceSelection());

            yield break;
        }

        [UnityTest]
        public IEnumerator DevicesChanged_SubscribeUnsubscribe_SafeAcrossDispose()
        {
            var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();

            Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> handler = (playout, recording) => { };
            platformAudio.DevicesChanged += handler;
            platformAudio.Dispose();

            Assert.DoesNotThrow(() => platformAudio.DevicesChanged -= handler);
            Assert.DoesNotThrow(() => platformAudio.DevicesChanged += handler);
            Assert.DoesNotThrow(() => platformAudio.Dispose());

            yield break;
        }

        [UnityTest]
        public IEnumerator CreateDisposeCreate_OneSession_Works()
        {
            // The native ADM is ref-counted across PlatformAudio instances; after a full
            // dispose the count must have returned to zero cleanly so a later instance in
            // the same session comes up working (an app's second call after tearing the
            // first one down).
            var first = PlatformAudioTestHelper.TryCreateOrIgnore();
            first.PlayoutPreference = new[] { AudioDeviceKind.Usb };
            first.Dispose();

            using var second = new PlatformAudio();
            Assert.DoesNotThrow(() => second.GetDevices());

            // Preference state is per instance: the first instance's mutation must not
            // leak into the fresh one.
            CollectionAssert.AreEqual(
                new[]
                {
                    AudioDeviceKind.Bluetooth,
                    AudioDeviceKind.WiredHeadset,
                    AudioDeviceKind.Speaker,
                    AudioDeviceKind.Earpiece,
                },
                second.PlayoutPreference);

            yield break;
        }

        [UnityTest]
        public IEnumerator PublicMembers_AfterDispose_ThrowObjectDisposed()
        {
            var platformAudio = PlatformAudioTestHelper.TryCreateOrIgnore();
            platformAudio.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = platformAudio.RecordingDeviceCount);
            Assert.Throws<ObjectDisposedException>(() => _ = platformAudio.PlayoutDeviceCount);
            Assert.Throws<ObjectDisposedException>(() => platformAudio.GetDevices());
            Assert.Throws<ObjectDisposedException>(() => _ = platformAudio.PlayoutPreference);
            Assert.Throws<ObjectDisposedException>(() =>
                platformAudio.PlayoutPreference = new[] { AudioDeviceKind.Speaker });
            Assert.Throws<ObjectDisposedException>(() => platformAudio.ClearPlayoutDeviceSelection());
            Assert.Throws<ObjectDisposedException>(() => platformAudio.SetRecordingDevice((uint)0));
            Assert.Throws<ObjectDisposedException>(() => platformAudio.SetRecordingDevice(""));
            Assert.Throws<ObjectDisposedException>(() => platformAudio.SetPlayoutDevice((uint)0));
            Assert.Throws<ObjectDisposedException>(() => platformAudio.SetPlayoutDevice(""));
            Assert.Throws<ObjectDisposedException>(() => platformAudio.StopRecording());

            // StartRecording is an iterator method: the guard throws on the first MoveNext.
            var start = platformAudio.StartRecording();
            Assert.Throws<ObjectDisposedException>(() => start.MoveNext());

            // The guards must not break dispose idempotency or event safety
            // (DevicesChanged_SubscribeUnsubscribe_SafeAcrossDispose covers the rest).
            Assert.DoesNotThrow(() => platformAudio.Dispose());

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
