#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using LiveKit.Internal;
using UnityEngine;

namespace LiveKit
{
    /// <summary>
    /// Android routing backend for <see cref="PlatformAudio"/>, built on the
    /// communication-device APIs introduced in Android 12 (API 31):
    /// <c>AudioManager.getAvailableCommunicationDevices</c> /
    /// <c>setCommunicationDevice</c> / <c>clearCommunicationDevice</c>.
    ///
    /// The controller owns the voice-communication audio session while session audio is
    /// enabled (<see cref="SetSessionAudioEnabled"/> — i.e. while a call is in progress):
    /// it enters <c>MODE_IN_COMMUNICATION</c> (saving and restoring the prior mode) and
    /// keeps the output route pinned to the best device — the sticky
    /// <see cref="SelectOutput"/> override while its device is still available, otherwise
    /// the highest-ranked available kind per the current
    /// <see cref="PlatformAudio.OutputPreference"/>. Owning the mode is what makes the
    /// pin authoritative: without it the platform periodically reasserts its own default
    /// route (observed on Pixel 8a: Telecom flipped playout back to the earpiece every
    /// ~6 s after a Bluetooth session ended). Note that since Android 13 the mode request
    /// is only honored while the app has active voice-communication capture, so
    /// <see cref="PlatformAudio.StartRecording"/> re-asserts the policy when capture
    /// (re)starts — through <see cref="ApplyOutputPreference"/>, which like every other
    /// re-evaluation path pins nothing while session audio is disabled.
    ///
    /// While session audio is disabled the session is handed back to the platform
    /// (communication device cleared, prior mode restored), so the mode request and the
    /// route pin cover the call rather than the lifetime of the instance. Enumeration,
    /// the change listener and the poll thread stay alive regardless, so
    /// <see cref="GetDevices"/> and <see cref="DevicesChanged"/> keep reporting the
    /// platform's own routing while idle.
    ///
    /// Route changes are detected two ways, both required (device-verified in the
    /// sample hotfix this backend is hardened from, PR #364):
    /// - <c>OnCommunicationDeviceChangedListener</c> — fires when the OS changes or
    ///   clears the pin (e.g. the pinned device disconnected).
    /// - A poll thread (every 1.5 s) — covers transitions that fire no event: a device
    ///   added while a pin is active, and the trace-verified teardown where a powered-off
    ///   Bluetooth headset stays in the available list up to ~10 s after the route
    ///   already fell back to the earpiece, then leaves the list without another
    ///   communication-device change.
    ///
    /// Threading: re-evaluation runs on whichever thread triggered it (Unity main,
    /// the Android main executor, or the poll thread — all JVM-attached) behind one
    /// lock. <see cref="DevicesChanged"/> may therefore be raised from any of them;
    /// <see cref="PlatformAudio"/> marshals it to the Unity main thread.
    /// </summary>
    internal sealed class AndroidRouteController : IRouteController
    {
        // android.media.AudioManager / AudioAttributes constants.
        private const int ModeInCommunication = 3;     // AudioManager.MODE_IN_COMMUNICATION
        private const int AudioFocusGain = 1;          // AudioManager.AUDIOFOCUS_GAIN
        private const int AudioFocusRequestGranted = 1; // AudioManager.AUDIOFOCUS_REQUEST_GRANTED
        private const int UsageVoiceCommunication = 2; // AudioAttributes.USAGE_VOICE_COMMUNICATION
        private const int ContentTypeSpeech = 1;       // AudioAttributes.CONTENT_TYPE_SPEECH

        private const int MinSupportedApiLevel = 31;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);
        // How long a pin is given to take effect before it is issued again. Selecting a
        // Bluetooth device starts an asynchronous SCO negotiation, and until it completes
        // the platform keeps reporting the previous communication device — so without this
        // the poll re-issues the pin into its own pending activation, which the platform
        // refuses ("BtHelper: requestScoState: failed to connect in state 1", device-verified
        // on a Pixel 8a / Android 16) and the route never arrives at all. Real route changes
        // come through the change listener, so this only slows down recovering from a pin the
        // platform dropped silently.
        private static readonly TimeSpan PinSettleTimeout = TimeSpan.FromSeconds(6);
        // Ceiling for the backoff applied when the platform keeps taking the pin without
        // acting on it — a state this SDK cannot clear (see the Bluetooth note in README).
        private static readonly TimeSpan PinSettleTimeoutMax = TimeSpan.FromSeconds(30);

        private readonly PlatformAudio _owner;
        private readonly object _gate = new object();
        private readonly ManualResetEventSlim _stopPoll = new ManualResetEventSlim(false);
        private readonly List<AudioDevice> _recordingSnapshot;
        private readonly Thread _pollThread;

        private List<AudioOutputKind> _ranked;
        private int _stickyDeviceId = -1;
        private int _pinnedDeviceId = -1;
        // When the outstanding pin was last issued — Stopwatch ticks, monotonic, so a
        // wall-clock step can neither cut the settle window short nor stretch it — to
        // give it _pinSettleTimeout to take effect, and whether the platform has been
        // seen honoring it since.
        private long _pinIssuedAtTimestamp;
        private bool _pinApplied;
        private TimeSpan _pinSettleTimeout = PinSettleTimeout;
        // Session audio starts enabled, matching the documented default of
        // PlatformAudio.SetSessionAudioEnabled (uniform with iOS).
        private bool _sessionAudioEnabled = true;
        private int _savedAudioMode;
        private bool _audioModeSaved;
        private CommunicationDeviceListener _listener;
        private AndroidJavaObject _audioFocusRequest;
        private bool _audioFocusEnabled;
        private List<(int Id, AudioOutputKind Kind, bool IsSelected)> _lastSignature;
        private bool _disposed;

        public event Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> DevicesChanged;

        /// <summary>
        /// Creates the Android backend, or an <see cref="UnsupportedRouteController"/> on
        /// Android versions below 12 (API 31), which lack the communication-device APIs
        /// this backend is built on. On those versions the routing verbs are documented
        /// no-ops/throws, matching the gate the sample hotfix carried.
        /// </summary>
        internal static IRouteController Create(PlatformAudio owner, IReadOnlyList<AudioOutputKind> initialPreference)
        {
            int sdkInt;
            try
            {
                sdkInt = AndroidSdkInt();
            }
            catch (Exception e)
            {
                Utils.Warning($"AndroidRouteController: failed to read Build.VERSION.SDK_INT, routing disabled: {e.Message}");
                return new UnsupportedRouteController(owner, "this Android device");
            }

            if (sdkInt < MinSupportedApiLevel)
                return new UnsupportedRouteController(owner, $"Android API {sdkInt} (routing requires API {MinSupportedApiLevel})");

            return new AndroidRouteController(owner, initialPreference);
        }

        private AndroidRouteController(PlatformAudio owner, IReadOnlyList<AudioOutputKind> initialPreference)
        {
            _owner = owner;
            _ranked = new List<AudioOutputKind>(initialPreference);

            // The FFI exposes a single placeholder entry for the OS default input on
            // Android; input routing follows the communication device, so this list is
            // static and can back every DevicesChanged payload. Fetched before any
            // session state is touched so a failure here has no side effects.
            _recordingSnapshot = owner.GetDevicesViaFfi().Recording;

            // Session audio defaults to enabled, so a fresh instance takes the call
            // session; apps that create PlatformAudio before their first call disable it
            // right after construction (see PlatformAudio.SetSessionAudioEnabled).
            EnterCommunicationMode();
            RegisterListener();
            Reevaluate();

            _pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Name = "LiveKitAndroidRoutePoll",
            };
            _pollThread.Start();
        }

        public (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevices()
        {
            var recording = _owner.GetDevicesViaFfi().Recording;
            var playout = new List<AudioDevice>();
            try
            {
                using var audioManager = GetAudioManager();
                using var current = audioManager.Call<AndroidJavaObject>("getCommunicationDevice");
                var currentId = current != null ? current.Call<int>("getId") : -1;

                using var available = audioManager.Call<AndroidJavaObject>("getAvailableCommunicationDevices");
                var count = available.Call<int>("size");
                for (var i = 0; i < count; i++)
                {
                    using var device = available.Call<AndroidJavaObject>("get", i);
                    playout.Add(ToAudioDevice(device, (uint)i, currentId));
                }
            }
            catch (Exception e)
            {
                Utils.Warning($"AndroidRouteController: device enumeration failed: {e.Message}");
            }
            return (recording, playout);
        }

        public void ApplyOutputPreference(IReadOnlyList<AudioOutputKind> ranked)
        {
            lock (_gate)
            {
                _ranked = new List<AudioOutputKind>(ranked);
            }
            Reevaluate();
        }

        public void SelectOutput(AudioDevice device)
        {
            if (string.IsNullOrEmpty(device.Guid)
                || !int.TryParse(device.Guid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                throw new ArgumentException(
                    $"Device '{device.Name}' does not carry an Android device id; " +
                    "pass an entry from GetDevices().Playout", nameof(device));

            lock (_gate)
            {
                _stickyDeviceId = id;
            }
            Reevaluate();
        }

        public void ClearOutputOverride()
        {
            lock (_gate)
            {
                if (_stickyDeviceId == -1)
                    return;
                _stickyDeviceId = -1;
            }
            Reevaluate();
        }

        /// <summary>
        /// Takes or hands back the call audio session: enabling enters
        /// <c>MODE_IN_COMMUNICATION</c> and lets the policy pin the route, disabling
        /// clears the pin and restores the mode this controller replaced. The ranked
        /// preference survives the transition unconditionally. The sticky override
        /// survives it only while its device stays available: the drop-on-disappear
        /// bookkeeping keeps running while the session is disabled, so a device that
        /// leaves the list between calls (a headset powered off) clears the override
        /// for good, and the next call routes by the ranked preference.
        /// </summary>
        public void SetSessionAudioEnabled(bool enabled)
        {
            lock (_gate)
            {
                if (_disposed || _sessionAudioEnabled == enabled)
                    return;
                _sessionAudioEnabled = enabled;
                if (enabled)
                    EnterCommunicationMode();
                else
                    LeaveCommunicationMode();
            }

            // Re-evaluate outside the lock (Reevaluate takes it): pin the policy's target
            // on enable, report the platform's own route on disable.
            Reevaluate();
        }

        /// <summary>
        /// Optional audio-focus request (AUDIOFOCUS_GAIN with voice-communication
        /// attributes) held while enabled. Off by default. Not exposed on the public
        /// API surface (PAR-019 defines it once); flip it here when embedding scenarios
        /// need focus, until a supported knob exists.
        /// </summary>
        internal bool AudioFocusEnabled
        {
            get
            {
                lock (_gate) return _audioFocusEnabled;
            }
            set
            {
                lock (_gate)
                {
                    if (_disposed || _audioFocusEnabled == value)
                        return;
                    _audioFocusEnabled = value;
                    if (value)
                        RequestAudioFocus();
                    else
                        AbandonAudioFocus();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            // Stop the poll first so no re-evaluation runs concurrently with teardown.
            _stopPoll.Set();
            if (_pollThread.Join(TimeSpan.FromSeconds(3)))
                _stopPoll.Dispose();
            else
                Utils.Warning("AndroidRouteController: poll thread did not stop in time");

            // Unregister BEFORE clearing the pin: clearCommunicationDevice fires the
            // change event, and a still-registered listener would immediately re-pin.
            UnregisterListener();

            lock (_gate)
            {
                AbandonAudioFocus();
                // Same idempotent release as a session-audio disable: clearing a pin we
                // no longer hold is a no-op, and the mode is only restored when this
                // controller is the one that replaced it.
                LeaveCommunicationMode();
                _sessionAudioEnabled = false;
            }
        }

        /// <summary>
        /// Single policy pass: picks the target device (sticky override while its device
        /// is still available — dropped for good once it disappears — else the best
        /// available kind by rank), pins it when it differs from the active route, and
        /// raises <see cref="DevicesChanged"/> when the observable list (ids, kinds,
        /// selection) changed since the last pass. Re-pinning is skipped when the target
        /// is already active: our own setCommunicationDevice fires the change listener,
        /// and that no-op check is what stops the feedback loop. When nothing sticky or
        /// ranked is available, an existing pin is released so the OS default applies;
        /// kinds missing from the ranking are never auto-selected.
        ///
        /// While session audio is disabled the pass is observation-only: it enumerates,
        /// keeps the sticky bookkeeping current and still raises
        /// <see cref="DevicesChanged"/>, but issues no setCommunicationDevice /
        /// clearCommunicationDevice and reports the platform's own communication device
        /// as the selected one. Every trigger — the change listener, the poll thread and
        /// the <see cref="PlatformAudio.StartRecording"/> re-assert — runs through here,
        /// so none of them can resurrect a released session.
        /// </summary>
        private void Reevaluate()
        {
            List<AudioDevice> playout = null;
            lock (_gate)
            {
                if (_disposed)
                    return;
                try
                {
                    using var audioManager = GetAudioManager();
                    using var current = audioManager.Call<AndroidJavaObject>("getCommunicationDevice");
                    var currentId = current != null ? current.Call<int>("getId") : -1;

                    using var available = audioManager.Call<AndroidJavaObject>("getAvailableCommunicationDevices");
                    var count = available.Call<int>("size");
                    var devices = new List<(AndroidJavaObject Device, int Id, AudioOutputKind Kind)>(count);
                    try
                    {
                        for (var i = 0; i < count; i++)
                        {
                            var device = available.Call<AndroidJavaObject>("get", i);
                            devices.Add((device, device.Call<int>("getId"), KindFromDeviceType(device.Call<int>("getType"))));
                        }

                        var targetIndex = -1;
                        if (_stickyDeviceId != -1)
                        {
                            targetIndex = devices.FindIndex(d => d.Id == _stickyDeviceId);
                            if (targetIndex < 0)
                            {
                                Utils.Debug("AndroidRouteController: sticky output device disappeared; reverting to automatic policy");
                                _stickyDeviceId = -1;
                            }
                        }

                        if (targetIndex < 0)
                        {
                            var bestRank = int.MaxValue;
                            for (var i = 0; i < devices.Count; i++)
                            {
                                var rank = _ranked.IndexOf(devices[i].Kind);
                                if (rank >= 0 && rank < bestRank)
                                {
                                    bestRank = rank;
                                    targetIndex = i;
                                }
                            }
                        }

                        int selectedId;
                        if (!_sessionAudioEnabled)
                        {
                            // No call in progress: report which device the platform would
                            // use for communication audio, and touch nothing. The target
                            // computed above is still worth running — it keeps the sticky
                            // override's "dropped once the device disappears" bookkeeping
                            // alive while idle — but it is only applied once a call
                            // re-enables the session.
                            selectedId = currentId;
                        }
                        else if (targetIndex >= 0)
                        {
                            var target = devices[targetIndex];
                            if (currentId == _pinnedDeviceId)
                                _pinApplied = true;
                            if (target.Id != currentId)
                            {
                                // A Bluetooth pin that has not taken effect yet is left to
                                // finish: setCommunicationDevice starts an asynchronous SCO
                                // negotiation there, and re-issuing lands in the platform's
                                // own pending activation and gets refused, so hammering it
                                // keeps the route from ever arriving. Once the pin has been
                                // seen applied, a later divergence is the platform dropping
                                // it (what the poll exists for) and is re-pinned at once.
                                // The other kinds apply without a negotiation, so a
                                // divergence there is always a dropped or ignored pin and
                                // is re-issued immediately, as before the settle window
                                // existed. See PinSettleTimeout, and _pinSettleTimeout for
                                // the backoff applied when the platform takes a Bluetooth
                                // pin but never acts on it.
                                var retry = _pinnedDeviceId == target.Id && !_pinApplied
                                    && target.Kind == AudioOutputKind.Bluetooth;
                                var settling = retry && ElapsedSincePinIssued() < _pinSettleTimeout;
                                if (settling)
                                {
                                    // Waiting on the negotiation: report the device the
                                    // platform still has, never the one merely requested.
                                    // The change listener re-runs this pass the moment the
                                    // pin lands, and the selection flip raises the
                                    // DevicesChanged for the real arrival.
                                    selectedId = currentId;
                                }
                                else
                                {
                                    var ok = audioManager.Call<bool>("setCommunicationDevice", target.Device);
                                    Utils.Debug($"AndroidRouteController: setCommunicationDevice(kind={target.Kind}) -> {ok}");
                                    // Stamped on every attempt, not only on success:
                                    // measured from a stale issue time the settle window
                                    // expires for good after one refused re-issue, and the
                                    // backoff decays into a warn+re-issue every poll tick.
                                    _pinIssuedAtTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                                    if (ok)
                                    {
                                        _pinnedDeviceId = target.Id;
                                        _pinApplied = false;
                                    }
                                    if (retry)
                                    {
                                        // The platform is taking the request and not acting on
                                        // it. Back off rather than keep asking: retrying into
                                        // an activation the platform will not start achieves
                                        // nothing, and the cause is usually outside this SDK
                                        // (see the Bluetooth note in the README).
                                        Utils.Warning(
                                            $"AndroidRouteController: the platform is not applying the route pin for " +
                                            $"{target.Kind} after {_pinSettleTimeout.TotalSeconds:0}s. If this is a " +
                                            "Bluetooth headset, another component in this process (Unity's audio engine " +
                                            "does this when it initializes with a headset connected) may hold an " +
                                            "outstanding startBluetoothSco request, which blocks the call link until it " +
                                            "resolves. Call audio stays on the previous output until then.");
                                        var next = TimeSpan.FromTicks(_pinSettleTimeout.Ticks * 2);
                                        _pinSettleTimeout = next > PinSettleTimeoutMax ? PinSettleTimeoutMax : next;
                                    }
                                    else
                                    {
                                        _pinSettleTimeout = PinSettleTimeout;
                                    }
                                    // Report the platform's answer, not the request: the
                                    // synchronous kinds are visible in this re-read right
                                    // away, while a pending Bluetooth pin must not be
                                    // announced as selected before it lands — GetDevices()
                                    // reads the same truth, and a premature "selected"
                                    // would also swallow the arrival event, because the
                                    // signature would never change again.
                                    using var applied = audioManager.Call<AndroidJavaObject>("getCommunicationDevice");
                                    selectedId = applied != null ? applied.Call<int>("getId") : -1;
                                    if (ok && selectedId == target.Id)
                                        _pinApplied = true;
                                }
                            }
                            else
                            {
                                selectedId = currentId;
                            }
                        }
                        else
                        {
                            if (_pinnedDeviceId != -1)
                            {
                                audioManager.Call("clearCommunicationDevice");
                                ResetPinTracking();
                                Utils.Debug("AndroidRouteController: no ranked device available; cleared pin, OS default applies");
                                using var fallback = audioManager.Call<AndroidJavaObject>("getCommunicationDevice");
                                selectedId = fallback != null ? fallback.Call<int>("getId") : -1;
                            }
                            else
                            {
                                selectedId = currentId;
                            }
                        }

                        var signature = new List<(int Id, AudioOutputKind Kind, bool IsSelected)>(devices.Count);
                        foreach (var d in devices)
                            signature.Add((d.Id, d.Kind, d.Id == selectedId));

                        if (SignatureChanged(signature))
                        {
                            _lastSignature = signature;
                            playout = new List<AudioDevice>(devices.Count);
                            for (var i = 0; i < devices.Count; i++)
                                playout.Add(ToAudioDevice(devices[i].Device, (uint)i, selectedId));
                        }
                    }
                    finally
                    {
                        foreach (var d in devices)
                            d.Device.Dispose();
                    }
                }
                catch (Exception e)
                {
                    Utils.Warning($"AndroidRouteController: route evaluation failed: {e.Message}");
                }
            }

            // Raised outside the lock; PlatformAudio marshals to the Unity main thread.
            if (playout != null)
                DevicesChanged?.Invoke(playout, new List<AudioDevice>(_recordingSnapshot));
        }

        // Called under _gate from both pin-release sites. Clears everything the
        // settle/backoff logic keys on; anything left behind resurfaces on the next
        // pin as a spurious settle-skip or backoff warning.
        private void ResetPinTracking()
        {
            _pinnedDeviceId = -1;
            _pinApplied = false;
            _pinSettleTimeout = PinSettleTimeout;
        }

        private TimeSpan ElapsedSincePinIssued()
        {
            var elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - _pinIssuedAtTimestamp;
            return TimeSpan.FromSeconds((double)elapsedTicks / System.Diagnostics.Stopwatch.Frequency);
        }

        private bool SignatureChanged(List<(int Id, AudioOutputKind Kind, bool IsSelected)> signature)
        {
            if (_lastSignature == null || _lastSignature.Count != signature.Count)
                return true;
            for (var i = 0; i < signature.Count; i++)
            {
                if (!_lastSignature[i].Equals(signature[i]))
                    return true;
            }
            return false;
        }

        private void PollLoop()
        {
            if (AndroidJNI.AttachCurrentThread() != 0)
            {
                Utils.Warning("AndroidRouteController: failed to attach poll thread to the JVM; poll disabled, only OS events will re-route");
                return;
            }
            try
            {
                while (!_stopPoll.Wait(PollInterval))
                    Reevaluate();
            }
            finally
            {
                AndroidJNI.DetachCurrentThread();
            }
        }

        // Both mode methods are called under _gate. The save/restore pairs up per
        // enable -> disable transition and is idempotent in both directions: the prior
        // mode is only captured when we do not already hold one, and it is only restored
        // when it was actually read from the platform — a failed read must never turn
        // into an unconditional MODE_NORMAL, which would stomp a mode this app does not
        // own (the rule the PAR-000 hotfix established).
        private void EnterCommunicationMode()
        {
            try
            {
                using var audioManager = GetAudioManager();
                if (!_audioModeSaved)
                {
                    _savedAudioMode = audioManager.Call<int>("getMode");
                    _audioModeSaved = true;
                }
                audioManager.Call("setMode", ModeInCommunication);
                Utils.Debug($"AndroidRouteController: audio mode -> MODE_IN_COMMUNICATION (was {_savedAudioMode})");
            }
            catch (Exception e)
            {
                Utils.Warning($"AndroidRouteController: failed to enter communication mode: {e.Message}");
            }
        }

        private void LeaveCommunicationMode()
        {
            try
            {
                using var audioManager = GetAudioManager();
                audioManager.Call("clearCommunicationDevice");
                ResetPinTracking();
                if (_audioModeSaved)
                {
                    audioManager.Call("setMode", _savedAudioMode);
                    _audioModeSaved = false;
                    Utils.Debug($"AndroidRouteController: route cleared, audio mode restored ({_savedAudioMode})");
                }
                else
                {
                    Utils.Debug("AndroidRouteController: route cleared, no saved audio mode to restore");
                }
            }
            catch (Exception e)
            {
                Utils.Warning($"AndroidRouteController: failed to release the audio session: {e.Message}");
            }
        }

        private void RegisterListener()
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                using var executor = activity.Call<AndroidJavaObject>("getMainExecutor");

                _listener = new CommunicationDeviceListener(this);
                audioManager.Call("addOnCommunicationDeviceChangedListener", executor, _listener);
            }
            catch (Exception e)
            {
                _listener = null;
                Utils.Warning($"AndroidRouteController: failed to register device listener, falling back to polling only: {e.Message}");
            }
        }

        private void UnregisterListener()
        {
            if (_listener == null)
                return;
            try
            {
                using var audioManager = GetAudioManager();
                audioManager.Call("removeOnCommunicationDeviceChangedListener", _listener);
            }
            catch (Exception e)
            {
                Utils.Warning($"AndroidRouteController: failed to unregister device listener: {e.Message}");
            }
            _listener = null;
        }

        // Both focus methods are called under _gate.
        private void RequestAudioFocus()
        {
            try
            {
                using var attributesBuilder = new AndroidJavaObject("android.media.AudioAttributes$Builder");
                using var withUsage = attributesBuilder.Call<AndroidJavaObject>("setUsage", UsageVoiceCommunication);
                using var withContentType = withUsage.Call<AndroidJavaObject>("setContentType", ContentTypeSpeech);
                using var attributes = withContentType.Call<AndroidJavaObject>("build");
                using var focusBuilder = new AndroidJavaObject("android.media.AudioFocusRequest$Builder", AudioFocusGain);
                using var withAttributes = focusBuilder.Call<AndroidJavaObject>("setAudioAttributes", attributes);
                _audioFocusRequest = withAttributes.Call<AndroidJavaObject>("build");

                using var audioManager = GetAudioManager();
                var result = audioManager.Call<int>("requestAudioFocus", _audioFocusRequest);
                Utils.Debug($"AndroidRouteController: requestAudioFocus -> {(result == AudioFocusRequestGranted ? "granted" : result.ToString())}");
            }
            catch (Exception e)
            {
                _audioFocusRequest?.Dispose();
                _audioFocusRequest = null;
                Utils.Warning($"AndroidRouteController: audio focus request failed: {e.Message}");
            }
        }

        private void AbandonAudioFocus()
        {
            if (_audioFocusRequest == null)
                return;
            try
            {
                using var audioManager = GetAudioManager();
                audioManager.Call<int>("abandonAudioFocusRequest", _audioFocusRequest);
            }
            catch (Exception e)
            {
                Utils.Warning($"AndroidRouteController: failed to abandon audio focus: {e.Message}");
            }
            _audioFocusRequest.Dispose();
            _audioFocusRequest = null;
        }

        private void OnCommunicationDeviceChangedFromJava()
        {
            try
            {
                Reevaluate();
            }
            catch (Exception e)
            {
                Utils.Warning($"AndroidRouteController: listener re-evaluation failed: {e.Message}");
            }
        }

        private static AudioDevice ToAudioDevice(AndroidJavaObject device, uint index, int selectedId)
        {
            var id = device.Call<int>("getId");
            using var productName = device.Call<AndroidJavaObject>("getProductName");
            return new AudioDevice
            {
                Index = index,
                Name = productName?.Call<string>("toString") ?? string.Empty,
                Guid = id.ToString(CultureInfo.InvariantCulture),
                Kind = KindFromDeviceType(device.Call<int>("getType")),
                IsSelected = id == selectedId,
            };
        }

        // AudioDeviceInfo.TYPE_* to AudioOutputKind, mirroring the planned FFI mapping.
        private static AudioOutputKind KindFromDeviceType(int deviceType)
        {
            switch (deviceType)
            {
                case 1: // TYPE_BUILTIN_EARPIECE
                    return AudioOutputKind.Earpiece;
                case 2: // TYPE_BUILTIN_SPEAKER
                    return AudioOutputKind.Speaker;
                case 3: // TYPE_WIRED_HEADSET
                case 4: // TYPE_WIRED_HEADPHONES
                    return AudioOutputKind.WiredHeadset;
                case 7: // TYPE_BLUETOOTH_SCO
                case 26: // TYPE_BLE_HEADSET
                case 27: // TYPE_BLE_SPEAKER
                    return AudioOutputKind.Bluetooth;
                case 22: // TYPE_USB_HEADSET
                    return AudioOutputKind.Usb;
                case 23: // TYPE_HEARING_AID
                    return AudioOutputKind.HearingAid;
                default:
                    return AudioOutputKind.Unknown;
            }
        }

        private static int AndroidSdkInt()
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            return version.GetStatic<int>("SDK_INT");
        }

        // Caller owns the returned object (wrap it in `using var`).
        private static AndroidJavaObject GetAudioManager()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            return activity.Call<AndroidJavaObject>("getSystemService", "audio");
        }

        // C#-side implementation of the Java callback interface. AndroidJavaProxy can
        // only implement interfaces, which is why this listens for communication-device
        // changes rather than subclassing android.media.AudioDeviceCallback (an abstract
        // class); list add/remove transitions that fire no communication-device event
        // are covered by the poll thread instead.
        private sealed class CommunicationDeviceListener : AndroidJavaProxy
        {
            private readonly AndroidRouteController _controller;

            public CommunicationDeviceListener(AndroidRouteController controller)
                : base("android.media.AudioManager$OnCommunicationDeviceChangedListener")
            {
                _controller = controller;
            }

            // Invoked by Android on the activity's main executor — a JVM-attached
            // thread, but not the Unity main thread.
            public void onCommunicationDeviceChanged(AndroidJavaObject device)
            {
                device?.Dispose();
                _controller.OnCommunicationDeviceChangedFromJava();
            }
        }
    }
}
#endif
