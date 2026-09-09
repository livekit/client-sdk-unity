using System;
using System.Collections.Generic;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    /// Backend seam for audio output routing. <see cref="PlatformAudio"/> registers one
    /// implementation per platform and forwards its public routing API
    /// (<see cref="PlatformAudio.PlayoutPreference"/>,
    /// <see cref="PlatformAudio.SetPlayoutDevice(string)"/>,
    /// <see cref="PlatformAudio.ClearPlayoutDeviceSelection"/>, <see cref="PlatformAudio.GetDevices"/>,
    /// <see cref="PlatformAudio.DevicesChanged"/>) through it, so the plumbing can be swapped
    /// per platform — and later wholesale for an FFI-backed implementation — without changing
    /// a public signature.
    /// </summary>
    internal interface IRouteController : IDisposable
    {
        /// <summary>Snapshot of the current recording and playout device lists.</summary>
        (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevices();

        /// <summary>Applies the ranked automatic output policy, most preferred first.</summary>
        void ApplyPlayoutPreference(IReadOnlyList<AudioDeviceKind> ranked);

        /// <summary>
        /// Routes output to the device with the given id (<see cref="AudioDevice.Guid"/>
        /// from <see cref="GetDevices"/>) as a sticky override of the automatic policy.
        /// Validation is the backend's job: desktop hands the id to the FFI, which checks
        /// it against the ADM's device list, Android checks it against the live
        /// communication-device list, and the backends without device selection ignore it
        /// with a warning.
        /// </summary>
        void SetPlayoutDevice(string deviceId);

        /// <summary>Clears the sticky override so the automatic policy applies again.</summary>
        void ClearPlayoutDeviceSelection();

        /// <summary>
        /// Signals whether a call is in progress, i.e. whether the backend may hold the
        /// platform's voice-communication audio session. Device enumeration and
        /// <see cref="DevicesChanged"/> must keep working while disabled.
        /// </summary>
        void SetSessionAudioEnabled(bool enabled);

        /// <summary>
        /// Raised when the available devices change, with the current (playout, recording)
        /// lists. May be raised from any thread; <see cref="PlatformAudio"/> marshals it to
        /// the Unity main thread before re-raising publicly.
        /// </summary>
        event Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> DevicesChanged;
    }

    /// <summary>
    /// Desktop routing backend: wraps the FFI device enumeration and per-device GUID
    /// selection. Ranked-kind policy is not implemented on desktop (output is chosen per
    /// device), and no desktop hot-plug events exist yet, so <see cref="DevicesChanged"/>
    /// is never raised.
    /// </summary>
    internal sealed class DesktopRouteController : IRouteController
    {
        private readonly PlatformAudio _owner;

        public DesktopRouteController(PlatformAudio owner)
        {
            _owner = owner;
        }

        public (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevices()
        {
            return _owner.GetDevicesViaFfi();
        }

        public void ApplyPlayoutPreference(IReadOnlyList<AudioDeviceKind> ranked)
        {
            // No routing effect on desktop: output is selected per device, not by kind.
        }

        public void SetPlayoutDevice(string deviceId)
        {
            // Straight to the FFI, as before the routing backends existed; it validates the
            // id against the ADM's device list (unknown id -> "Device not found").
            _owner.SetPlayoutDeviceViaFfi(deviceId);
        }

        public void ClearPlayoutDeviceSelection()
        {
            // No automatic policy to fall back to on desktop; the selected device stays.
        }

        public void SetSessionAudioEnabled(bool enabled)
        {
            // No call session to hold on desktop: the ADM owns the devices directly.
        }

        public event Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> DevicesChanged
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Placeholder backend for platforms without a routing implementation: Android below
    /// API 31 (which lacks the communication-device APIs the Android backend is built
    /// on). Device snapshots still work through the FFI (a single placeholder entry for
    /// the OS default input/output); the routing verbs no-op as documented on the public
    /// API.
    /// </summary>
    internal sealed class UnsupportedRouteController : IRouteController
    {
        private readonly PlatformAudio _owner;
        private readonly string _platform;

        public UnsupportedRouteController(PlatformAudio owner, string platform)
        {
            _owner = owner;
            _platform = platform;
        }

        public (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevices()
        {
            return _owner.GetDevicesViaFfi();
        }

        public void ApplyPlayoutPreference(IReadOnlyList<AudioDeviceKind> ranked)
        {
            // Stored by PlatformAudio; no routing effect until this platform's backend lands.
        }

        public void SetPlayoutDevice(string deviceId)
        {
            Utils.Warning(
                $"PlatformAudio.SetPlayoutDevice has no effect on {_platform}: the OS owns output routing.");
        }

        public void ClearPlayoutDeviceSelection()
        {
            // No override can exist on this platform: SetPlayoutDevice is ignored.
        }

        public void SetSessionAudioEnabled(bool enabled)
        {
            // Nothing to gate: this platform has no routing backend holding a session.
        }

        public event Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> DevicesChanged
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }
}
