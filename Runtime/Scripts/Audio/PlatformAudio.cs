using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using LiveKit.Proto;
using LiveKit.Internal;
using LiveKit.Internal.FFI.Requests;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

using LiveKit.Internal.FFI;
namespace LiveKit
{
#if UNITY_IOS && !UNITY_EDITOR
    internal static class IOSAudioSessionHelper
    {
        /// <summary>
        /// Configures the iOS audio session for VoIP/WebRTC.
        /// Must be called before creating PlatformAudio.
        /// </summary>
        [DllImport("__Internal")]
        internal static extern void LiveKit_ConfigureAudioSessionForVoIP();

        /// <summary>
        /// Restores the audio session Unity had before LiveKit configured it
        /// (or the ambient category as a fallback) and reactivates it so Unity
        /// audio output resumes. Called when the last PlatformAudio is disposed.
        /// </summary>
        [DllImport("__Internal")]
        internal static extern void LiveKit_RestoreDefaultAudioSession();

        /// <summary>
        /// Enables or disables WebRTC's VPIO audio unit while the app keeps
        /// ownership of the audio session. Enable when a call connects, disable
        /// when it ends. Disabling on hang-up stops call audio without
        /// deactivating the session, so other app audio keeps playing.
        /// </summary>
        [DllImport("__Internal")]
        internal static extern void LiveKit_SetAudioEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);
    }
#endif

    /// <summary>
    /// The kind of audio output device, used for ranked routing policies on mobile
    /// platforms (see <see cref="PlatformAudio.OutputPreference"/>).
    ///
    /// The numeric values mirror the planned FFI protocol enum (AudioDeviceKind) one-to-one
    /// so a future FFI-backed implementation maps without translation. Do not renumber.
    /// </summary>
    public enum AudioOutputKind
    {
        /// <summary>The platform did not report a device type.</summary>
        Unknown = 0,
        /// <summary>The phone's built-in earpiece (receiver).</summary>
        Earpiece = 1,
        /// <summary>The built-in loudspeaker.</summary>
        Speaker = 2,
        /// <summary>A wired headset or headphones.</summary>
        WiredHeadset = 3,
        /// <summary>A Bluetooth audio device.</summary>
        Bluetooth = 4,
        /// <summary>A USB audio device.</summary>
        Usb = 5,
        /// <summary>A hearing aid.</summary>
        HearingAid = 6,
    }

    /// <summary>
    /// Information about an audio device (microphone or speaker).
    /// </summary>
    public struct AudioDevice
    {
        /// <summary>Device index (0-based). Note: indices can change when devices are added/removed.</summary>
        public uint Index;
        /// <summary>Device name as reported by the operating system.</summary>
        public string Name;
        /// <summary>
        /// Platform-specific unique device identifier (GUID).
        /// This is stable across device additions/removals and should be preferred
        /// over index for device selection.
        /// </summary>
        public string Guid;
        /// <summary>
        /// The kind of output this device represents. <see cref="AudioOutputKind.Unknown"/>
        /// where the platform does not report a type — currently all devices: no routing
        /// backend classifies devices yet.
        /// </summary>
        public AudioOutputKind Kind;
        /// <summary>
        /// Whether this device is the active output route. Only meaningful once a platform
        /// routing backend reports selection state — currently always false.
        /// </summary>
        public bool IsSelected;
    }

    /// <summary>
    /// Platform audio device management using WebRTC's Audio Device Module (ADM).
    ///
    /// PlatformAudio provides access to the platform's audio devices (microphones and
    /// speakers) and enables automatic audio capture and playback through WebRTC's ADM.
    ///
    /// Key features:
    /// - Echo cancellation (AEC)
    /// - Automatic gain control (AGC)
    /// - Noise suppression (NS)
    /// - Automatic speaker playout for remote audio
    ///
    /// Usage:
    /// 1. Create a PlatformAudio instance (enables ADM)
    /// 2. Optionally enumerate and select devices
    /// 3. Create audio tracks using PlatformAudioSource
    /// 4. Remote audio automatically plays through speakers
    
    public sealed class PlatformAudio : IDisposable
    {
        internal readonly FfiHandle Handle;
        private readonly PlatformAudioInfo _info;
        private readonly IRouteController _routeController;
        private readonly SynchronizationContext _syncContext;
        private List<AudioOutputKind> _outputPreference = new List<AudioOutputKind>(DefaultOutputPreference);
        private bool _disposed = false;
#if UNITY_IOS && !UNITY_EDITOR
        // Tracks live PlatformAudio instances so the iOS audio session is restored
        // only when the last one is disposed (aligned with the native ADM ref-count).
        private static int _instanceCount;
#endif

        private static readonly AudioOutputKind[] DefaultOutputPreference =
        {
            AudioOutputKind.Bluetooth,
            AudioOutputKind.WiredHeadset,
            AudioOutputKind.Speaker,
            AudioOutputKind.Earpiece,
        };

        /// <summary>
        /// Number of available recording (microphone) devices.
        /// </summary>
        public int RecordingDeviceCount => _info.RecordingDeviceCount;

        /// <summary>
        /// Number of available playout (speaker) devices.
        /// </summary>
        public int PlayoutDeviceCount => _info.PlayoutDeviceCount;

        /// <summary>
        /// Creates a new PlatformAudio instance, enabling the platform ADM.
        ///
        /// This must be called before creating any PlatformAudioSource or connecting
        /// to a room if you want automatic speaker playout for remote audio.
        ///
        /// On iOS, this automatically configures the audio session for VoIP mode
        /// (PlayAndRecord category with VideoChat mode) to enable hardware echo
        /// cancellation and microphone input.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the platform ADM could not be initialized (e.g., no audio devices,
        /// missing permissions).
        /// </exception>
        public PlatformAudio()
        {
#if UNITY_IOS && !UNITY_EDITOR
            // Configure iOS audio session for VoIP before initializing WebRTC ADM.
            // This sets PlayAndRecord category with VideoChat mode for hardware AEC.
            IOSAudioSessionHelper.LiveKit_ConfigureAudioSessionForVoIP();
#endif

            using var request = FFIBridge.Instance.NewRequest<NewPlatformAudioRequest>();
            using var response = request.Send();
            FfiResponse res = response;

            if (res.NewPlatformAudio.MessageCase == NewPlatformAudioResponse.MessageOneofCase.Error)
                throw new InvalidOperationException($"Failed to create PlatformAudio: {res.NewPlatformAudio.Error}");

            var platformAudio = res.NewPlatformAudio.PlatformAudio;
            Handle = FfiHandle.FromOwnedHandle(platformAudio.Handle);
            _info = platformAudio.Info;

            _syncContext = SynchronizationContext.Current;
            _routeController = CreateRouteController();
            _routeController.DevicesChanged += OnRouteControllerDevicesChanged;

            Utils.Debug($"PlatformAudio created: {RecordingDeviceCount} recording devices, {PlayoutDeviceCount} playout devices");

#if UNITY_IOS && !UNITY_EDITOR
            // Count this instance only after successful construction so a failed
            // ctor never leaves the counter stuck above zero.
            System.Threading.Interlocked.Increment(ref _instanceCount);
#endif
        }

        private IRouteController CreateRouteController()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new UnsupportedRouteController(this, "Android");
#elif UNITY_IOS && !UNITY_EDITOR
            return new UnsupportedRouteController(this, "iOS");
#else
            return new DesktopRouteController(this);
#endif
        }

        /// <summary>
        /// Gets the lists of available recording and playout devices.
        ///
        /// Platform behavior:
        /// - Desktop (Windows/macOS/Linux): returns the full list of microphones and
        ///   speakers reported by the OS. Devices can be selected with
        ///   <see cref="SetRecordingDevice(string)"/> / <see cref="SetPlayoutDevice(string)"/>.
        /// - iOS and Android: returns a single placeholder entry at index 0 for each
        ///   list, representing the system's currently selected default input/output.
        ///   The OS owns audio routing on these platforms (AVAudioSession on iOS,
        ///   AudioManager on Android), so individual devices are not enumerated and
        ///   selecting one is a no-op (see <see cref="SetRecordingDevice(string)"/> /
        ///   <see cref="SetPlayoutDevice(string)"/>).
        /// </summary>
        /// <returns>
        /// A tuple containing:
        /// - Recording: List of available microphones (on iOS/Android, a single
        ///   placeholder for the OS default input)
        /// - Playout: List of available speakers/headphones (on iOS/Android, a single
        ///   placeholder for the OS default output)
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if device enumeration failed.
        /// </exception>
        public (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevices()
        {
            return _routeController.GetDevices();
        }

        /// <summary>
        /// Device enumeration through the FFI, shared by the route controllers.
        /// <see cref="AudioDevice.Kind"/> and <see cref="AudioDevice.IsSelected"/> are not
        /// reported by the FFI and stay at their defaults (Unknown / false).
        /// </summary>
        internal (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevicesViaFfi()
        {
            using var request = FFIBridge.Instance.NewRequest<GetAudioDevicesRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;

            if (res.GetAudioDevices.HasError && !string.IsNullOrEmpty(res.GetAudioDevices.Error))
                throw new InvalidOperationException($"Failed to get audio devices: {res.GetAudioDevices.Error}");

            var recording = new List<AudioDevice>();
            foreach (var device in res.GetAudioDevices.RecordingDevices)
            {
                recording.Add(new AudioDevice {
                    Index = device.Index,
                    Name = device.Name,
                    Guid = device.HasGuid ? device.Guid : null
                });
            }

            var playout = new List<AudioDevice>();
            foreach (var device in res.GetAudioDevices.PlayoutDevices)
            {
                playout.Add(new AudioDevice {
                    Index = device.Index,
                    Name = device.Name,
                    Guid = device.HasGuid ? device.Guid : null
                });
            }

            return (recording, playout);
        }

        /// <summary>
        /// Ranked automatic output routing policy, most preferred first. When no explicit
        /// output override is active (<see cref="SelectOutput"/>), the platform routes to
        /// the highest-ranked kind that has a connected device.
        ///
        /// Default: Bluetooth > WiredHeadset > Speaker > Earpiece.
        ///
        /// Precedence with <see cref="IsSpeakerOutputPreferred"/>: this list is the single
        /// source of truth; the bool is convenience sugar that only rewrites the relative
        /// order of <see cref="AudioOutputKind.Speaker"/> and
        /// <see cref="AudioOutputKind.Earpiece"/> inside this list, and reading the bool
        /// reads their current relative order. There is no separate speaker-preference state.
        ///
        /// Platform notes: on iOS, external devices (Bluetooth, wired) always take priority
        /// over the built-in outputs, so the Speaker/Earpiece relative order — i.e.
        /// <see cref="IsSpeakerOutputPreferred"/> — is the only part of the ranking with an
        /// effect. On Android the full ranking applies. On desktop, output is selected
        /// per device (<see cref="SelectOutput"/> / <see cref="SetPlayoutDevice(string)"/>)
        /// and the ranking has no routing effect. The mobile routing backends are not
        /// implemented yet in this version: on Android and iOS the value is currently
        /// stored and round-trips, but has no routing effect either.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown if set to null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown if the list contains <see cref="AudioOutputKind.Unknown"/> or duplicates.
        /// </exception>
        public IReadOnlyList<AudioOutputKind> OutputPreference
        {
            get => _outputPreference.AsReadOnly();
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                var ranked = new List<AudioOutputKind>(value.Count);
                foreach (var kind in value)
                {
                    if (kind == AudioOutputKind.Unknown)
                        throw new ArgumentException(
                            "OutputPreference cannot contain AudioOutputKind.Unknown", nameof(value));
                    if (ranked.Contains(kind))
                        throw new ArgumentException(
                            $"OutputPreference contains {kind} more than once", nameof(value));
                    ranked.Add(kind);
                }

                _outputPreference = ranked;
                _routeController.ApplyOutputPreference(_outputPreference.AsReadOnly());
            }
        }

        /// <summary>
        /// Whether the loudspeaker is preferred over the earpiece for automatic routing.
        ///
        /// Precedence with <see cref="OutputPreference"/>: the list is the single source of
        /// truth; this bool is convenience sugar that only rewrites the relative order of
        /// <see cref="AudioOutputKind.Speaker"/> and <see cref="AudioOutputKind.Earpiece"/>
        /// inside <see cref="OutputPreference"/>, and reading it reads their current
        /// relative order. There is no separate speaker-preference state. Reading returns
        /// true when Speaker ranks ahead of Earpiece (or Earpiece is absent), false when
        /// Speaker is absent. Setting reorders the pair in place at the position of
        /// whichever currently ranks first, inserting a missing kind next to the present
        /// one (or appending both when neither is listed) so the value round-trips.
        ///
        /// Platform notes: on iOS, external devices (Bluetooth, wired) always take priority
        /// over the built-in outputs, so this bool is the only part of the ranking with an
        /// effect. On Android the full ranking applies. On desktop, output is selected
        /// per device (<see cref="SelectOutput"/> / <see cref="SetPlayoutDevice(string)"/>)
        /// and the ranking has no routing effect. The mobile routing backends are not
        /// implemented yet in this version: on Android and iOS the value is currently
        /// stored and round-trips, but has no routing effect either.
        /// </summary>
        public bool IsSpeakerOutputPreferred
        {
            get
            {
                var speaker = _outputPreference.IndexOf(AudioOutputKind.Speaker);
                var earpiece = _outputPreference.IndexOf(AudioOutputKind.Earpiece);
                if (speaker < 0) return false;
                return earpiece < 0 || speaker < earpiece;
            }
            set
            {
                var first = value ? AudioOutputKind.Speaker : AudioOutputKind.Earpiece;
                var second = value ? AudioOutputKind.Earpiece : AudioOutputKind.Speaker;

                var reordered = new List<AudioOutputKind>(_outputPreference.Count + 2);
                var pairInserted = false;
                foreach (var kind in _outputPreference)
                {
                    if (kind == AudioOutputKind.Speaker || kind == AudioOutputKind.Earpiece)
                    {
                        if (!pairInserted)
                        {
                            reordered.Add(first);
                            reordered.Add(second);
                            pairInserted = true;
                        }
                        continue;
                    }
                    reordered.Add(kind);
                }
                if (!pairInserted)
                {
                    reordered.Add(first);
                    reordered.Add(second);
                }

                _outputPreference = reordered;
                _routeController.ApplyOutputPreference(_outputPreference.AsReadOnly());
            }
        }

        /// <summary>
        /// Routes audio output to the given device as a sticky override of the automatic
        /// <see cref="OutputPreference"/> policy: the route stays on the device until
        /// <see cref="ClearOutputOverride"/> is called. The device is matched against the
        /// current <see cref="GetDevices"/> playout list by <see cref="AudioDevice.Guid"/>
        /// when set, otherwise by index and name.
        ///
        /// Platform notes: on desktop this selects the device like
        /// <see cref="SetPlayoutDevice(string)"/>. On Android and iOS the routing backends
        /// are not implemented yet in this version and this method throws
        /// <see cref="NotSupportedException"/>.
        /// </summary>
        /// <param name="device">A playout device from <see cref="GetDevices"/>.</param>
        /// <exception cref="ArgumentException">
        /// Thrown if the device does not match any current playout device.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// Thrown on Android and iOS, where no routing backend exists yet.
        /// </exception>
        public void SelectOutput(AudioDevice device)
        {
            var (_, playout) = GetDevices();
            foreach (var candidate in playout)
            {
                var matches = !string.IsNullOrEmpty(device.Guid)
                    ? candidate.Guid == device.Guid
                    : candidate.Index == device.Index && candidate.Name == device.Name;
                if (!matches) continue;

                _routeController.SelectOutput(candidate);
                return;
            }

            throw new ArgumentException(
                $"Device '{device.Name}' (index {device.Index}, guid {device.Guid ?? "none"}) " +
                "is not a current playout device", nameof(device));
        }

        /// <summary>
        /// Clears the sticky override set by <see cref="SelectOutput"/> so the automatic
        /// <see cref="OutputPreference"/> policy applies again.
        ///
        /// Platform notes: on desktop there is no automatic policy to fall back to yet, so
        /// clearing keeps the currently selected device (no-op). On Android and iOS no
        /// override can exist yet (<see cref="SelectOutput"/> throws), so this is a no-op
        /// there as well.
        /// </summary>
        public void ClearOutputOverride()
        {
            _routeController.ClearOutputOverride();
        }

        /// <summary>
        /// Raised when the set of available audio devices changes, with the current playout
        /// and recording device lists. Raised on the Unity main thread.
        ///
        /// No implementation raises this event yet in this version: desktop hot-plug events
        /// and the mobile routing backends that produce it are not implemented. Subscribing
        /// and unsubscribing is safe at any time, including after <see cref="Dispose"/>.
        /// </summary>
        public event Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> DevicesChanged;

        private void OnRouteControllerDevicesChanged(
            IReadOnlyList<AudioDevice> playout, IReadOnlyList<AudioDevice> recording)
        {
            if (_disposed) return;

            if (_syncContext != null && _syncContext != SynchronizationContext.Current)
            {
                _syncContext.Post(_ =>
                {
                    if (!_disposed)
                        DevicesChanged?.Invoke(playout, recording);
                }, null);
                return;
            }

            DevicesChanged?.Invoke(playout, recording);
        }

        /// <summary>
        /// Sets the recording device (microphone) by index.
        ///
        /// Convenience wrapper around <see cref="SetRecordingDevice(string)"/> that looks
        /// up the GUID from <see cref="GetDevices"/>. Prefer the GUID overload for code
        /// that persists a selection — indices can shift when devices are added/removed.
        /// </summary>
        /// <param name="index">Device index from GetDevices().Recording</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device index is out of range or the operation failed.
        /// </exception>
        public void SetRecordingDevice(uint index)
        {
            var (recording, _) = GetDevices();
            if (index >= recording.Count)
                throw new InvalidOperationException($"Recording device index {index} out of range (max: {recording.Count - 1})");

            SetRecordingDevice(recording[(int)index].Guid ?? "");
        }

        /// <summary>
        /// Sets the recording device (microphone) by device ID (GUID).
        ///
        /// On Android and iOS this is a no-op in the native ADM: input routing is
        /// governed by the OS (AVAudioSession on iOS, AudioManager on Android) and
        /// the call is acknowledged but ignored. The method is still safe to call,
        /// and the response carries no error. <see cref="GetDevices"/> only exposes a
        /// single placeholder entry (index 0) for the OS default input on these
        /// platforms, so there is nothing else to select.
        /// </summary>
        /// <param name="deviceId">Device ID/GUID from GetDevices().Recording[i].Guid</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device is not found or the operation failed.
        /// </exception>
        public void SetRecordingDevice(string deviceId)
        {
            using var request = FFIBridge.Instance.NewRequest<SetRecordingDeviceRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();
            request.request.DeviceId = deviceId;

            using var response = request.Send();
            FfiResponse res = response;

            if (res.SetRecordingDevice.HasError && !string.IsNullOrEmpty(res.SetRecordingDevice.Error))
                throw new InvalidOperationException($"Failed to set recording device: {res.SetRecordingDevice.Error}");

            Utils.Debug($"PlatformAudio: set recording device to {deviceId}");
        }

        /// <summary>
        /// Sets the playout device (speaker/headphones) by index.
        ///
        /// Convenience wrapper around <see cref="SetPlayoutDevice(string)"/> that looks
        /// up the GUID from <see cref="GetDevices"/>. Prefer the GUID overload for code
        /// that persists a selection — indices can shift when devices are added/removed.
        /// </summary>
        /// <param name="index">Device index from GetDevices().Playout</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device index is out of range or the operation failed.
        /// </exception>
        public void SetPlayoutDevice(uint index)
        {
            var (_, playout) = GetDevices();
            if (index >= playout.Count)
                throw new InvalidOperationException($"Playout device index {index} out of range (max: {playout.Count - 1})");

            SetPlayoutDevice(playout[(int)index].Guid ?? "");
        }

        /// <summary>
        /// Sets the playout device (speaker/headphones) by device ID (GUID).
        ///
        /// On Android and iOS this is a no-op in the native ADM: output routing is
        /// governed by the OS (AVAudioSession on iOS, AudioManager on Android) and
        /// the call is acknowledged but ignored. The method is still safe to call,
        /// and the response carries no error. <see cref="GetDevices"/> only exposes a
        /// single placeholder entry (index 0) for the OS default output on these
        /// platforms, so there is nothing else to select.
        /// </summary>
        /// <param name="deviceId">Device ID/GUID from GetDevices().Playout[i].Guid</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the device is not found or the operation failed.
        /// </exception>
        public void SetPlayoutDevice(string deviceId)
        {
            using var request = FFIBridge.Instance.NewRequest<SetPlayoutDeviceRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();
            request.request.DeviceId = deviceId;

            using var response = request.Send();
            FfiResponse res = response;

            if (res.SetPlayoutDevice.HasError && !string.IsNullOrEmpty(res.SetPlayoutDevice.Error))
                throw new InvalidOperationException($"Failed to set playout device: {res.SetPlayoutDevice.Error}");

            Utils.Debug($"PlatformAudio: set playout device to {deviceId}");
        }

        /// <summary>
        /// Starts recording from the microphone.
        ///
        /// Recording is started automatically when PlatformAudio is created.
        /// Use this to resume recording after calling StopRecording.
        /// This turns on the system's recording privacy indicator (e.g., on macOS/iOS).
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the operation failed.
        /// </exception>
        public IEnumerator StartRecording()
        {
#if PLATFORM_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                // Fire the system permission dialog and yield until the user resolves it.
                // PermissionCallbacks delivers the result asynchronously from the Android OS;
                // we poll the captured flag from this coroutine until one of the callbacks
                // sets it. Without this gate, the WebRTC Android ADM would crash the process
                // when AudioRecord fails to open due to the missing permission.
                bool? granted = null;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => granted = true;
                callbacks.PermissionDenied += _ => granted = false;
                callbacks.PermissionDeniedAndDontAskAgain += _ => granted = false;
                Permission.RequestUserPermission(Permission.Microphone, callbacks);

                while (granted == null)
                    yield return null;

                if (granted == false)
                    throw new InvalidOperationException(
                        "Microphone permission denied by user; cannot start recording.");
            }
#endif

            using var request = FFIBridge.Instance.NewRequest<StartRecordingRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;

            if (res.StartRecording.HasError && !string.IsNullOrEmpty(res.StartRecording.Error))
                throw new InvalidOperationException($"Failed to start recording: {res.StartRecording.Error}");

            Utils.Debug("PlatformAudio: started recording");

            // Ensures this method is always a valid iterator even when the PLATFORM_ANDROID
            // branch is compiled out (no `yield return` would otherwise be reachable on
            // non-Android builds, which is a compile error for IEnumerator-returning methods).
            yield break;
        }

        /// <summary>
        /// Stops recording from the microphone.
        ///
        /// Use this to temporarily stop recording without disposing PlatformAudio.
        /// This turns off the system's recording privacy indicator (e.g., on macOS/iOS).
        /// Call StartRecording to resume recording.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the operation failed.
        /// </exception>
        public void StopRecording()
        {
            using var request = FFIBridge.Instance.NewRequest<StopRecordingRequest>();
            request.request.PlatformAudioHandle = (ulong)Handle.DangerousGetHandle();

            using var response = request.Send();
            FfiResponse res = response;

            if (res.StopRecording.HasError && !string.IsNullOrEmpty(res.StopRecording.Error))
                throw new InvalidOperationException($"Failed to stop recording: {res.StopRecording.Error}");

            Utils.Debug("PlatformAudio: stopped recording");
        }

        /// <summary>
        /// Signals whether call audio should be active on the platform audio session.
        ///
        /// On iOS this gates WebRTC's VPIO audio unit while the app retains ownership
        /// of the shared AVAudioSession. It is enabled by default when PlatformAudio is
        /// created, so this only needs to be called to <c>false</c> when leaving a room
        /// (and back to <c>true</c> when rejoining). Disabling stops the microphone/
        /// remote audio path and the hardware voice processing, but keeps the audio
        /// session active so other Unity audio (e.g. background music) is not
        /// interrupted — which is why Unity audio survives a hang-up.
        ///
        /// On other platforms this is a no-op: the OS/ADM manages the session directly.
        /// </summary>
        /// <param name="enabled">True while a call is active, false otherwise.</param>
        public void SetSessionAudioEnabled(bool enabled)
        {
#if UNITY_IOS && !UNITY_EDITOR
            IOSAudioSessionHelper.LiveKit_SetAudioEnabled(enabled);
#endif
            Utils.Debug($"PlatformAudio: session audio enabled={enabled}");
        }

        /// <summary>
        /// Releases the PlatformAudio resources.
        ///
        /// When disposed, the platform ADM may be disabled if this was the last
        /// PlatformAudio instance.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _routeController.DevicesChanged -= OnRouteControllerDevicesChanged;
            _routeController.Dispose();
            Handle.Dispose();

#if UNITY_IOS && !UNITY_EDITOR
            // Once the last instance is gone, relinquish the app-owned audio session:
            // disable call audio, release our activation, leave manual mode, restore
            // the session Unity had before LiveKit touched it, and reactivate it so
            // Unity audio output resumes. Balances LiveKit_ConfigureAudioSessionForVoIP()
            // in the constructor so the session isn't left stuck in PlayAndRecord.
            if (System.Threading.Interlocked.Decrement(ref _instanceCount) == 0)
                IOSAudioSessionHelper.LiveKit_RestoreDefaultAudioSession();
#endif

            _disposed = true;
            Utils.Debug("PlatformAudio disposed");
        }
    }
}
