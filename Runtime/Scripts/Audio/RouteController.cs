using System;
using System.Collections.Generic;

namespace LiveKit
{
    /// <summary>
    /// Backend seam for audio output routing. <see cref="PlatformAudio"/> registers one
    /// implementation per platform and forwards its public routing API
    /// (<see cref="PlatformAudio.OutputPreference"/>, <see cref="PlatformAudio.SelectOutput"/>,
    /// <see cref="PlatformAudio.ClearOutputOverride"/>, <see cref="PlatformAudio.GetDevices"/>,
    /// <see cref="PlatformAudio.DevicesChanged"/>) through it, so the plumbing can be swapped
    /// per platform — and later wholesale for an FFI-backed implementation — without changing
    /// a public signature.
    /// </summary>
    internal interface IRouteController : IDisposable
    {
        /// <summary>Snapshot of the current recording and playout device lists.</summary>
        (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevices();

        /// <summary>Applies the ranked automatic output policy, most preferred first.</summary>
        void ApplyOutputPreference(IReadOnlyList<AudioOutputKind> ranked);

        /// <summary>
        /// Routes output to the given device as a sticky override of the automatic policy.
        /// The device has already been validated against the current playout snapshot.
        /// </summary>
        void SelectOutput(AudioDevice device);

        /// <summary>Clears the sticky override so the automatic policy applies again.</summary>
        void ClearOutputOverride();

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

        public void ApplyOutputPreference(IReadOnlyList<AudioOutputKind> ranked)
        {
            // No routing effect on desktop: output is selected per device, not by kind.
        }

        public void SelectOutput(AudioDevice device)
        {
            if (!string.IsNullOrEmpty(device.Guid))
                _owner.SetPlayoutDevice(device.Guid);
            else
                _owner.SetPlayoutDevice(device.Index);
        }

        public void ClearOutputOverride()
        {
            // No automatic policy to fall back to on desktop; the selected device stays.
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
    /// Placeholder backend for platforms whose routing implementation has not landed yet
    /// (Android). Device snapshots still work through the FFI (a single placeholder
    /// entry for the OS default input/output); the routing verbs throw or no-op as
    /// documented on the public API.
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

        public void ApplyOutputPreference(IReadOnlyList<AudioOutputKind> ranked)
        {
            // Stored by PlatformAudio; no routing effect until this platform's backend lands.
        }

        public void SelectOutput(AudioDevice device)
        {
            throw new NotSupportedException(
                $"SelectOutput is not implemented on {_platform} yet");
        }

        public void ClearOutputOverride()
        {
            // No override can exist on this platform: SelectOutput throws.
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
