using System;
using System.Collections;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

// Drives the duplex platform audio (WebRTC ADM): captures the default microphone with
// the configured audio processing (AEC/NS/AGC) and publishes it as a LiveKit track, and
// selects the default playout device through which remote tracks are played back
// automatically. Publish/Unpublish can be cycled (e.g. a mute toggle) while the ADM stays
// alive; Dispose tears everything down in dependency order. On Android the controller
// also owns the voice-communication audio session (mode + output route, loudspeaker
// over earpiece) for its whole lifetime; an active mic capture is what makes that
// session authoritative, so start it with StartCapture when the call begins (even
// when joining muted) — it then stays open across mute cycles until StopCapture when
// the call ends. See StartCapture, SetupAndroidCommunicationAudio and Unpublish.
public sealed class PlatformAudioController : IDisposable
{
    readonly string _trackName;
    readonly AudioProcessingOptions _audioOptions;

    PlatformAudio _platformAudio;
    PlatformAudioSource _source;
    LocalAudioTrack _track;
    Room _room;
    bool _isRecording;

#if UNITY_ANDROID && !UNITY_EDITOR
    CommunicationDeviceListener _routeListener;
    int _savedAudioMode; // MODE_NORMAL unless something else was active at Initialize
#endif

    public bool IsInitialized => _platformAudio != null;
    public bool IsPublished { get; private set; }

    public PlatformAudioController(string trackName, AudioProcessingOptions audioOptions)
    {
        _trackName = trackName;
        _audioOptions = audioOptions;
    }

    // Creates the WebRTC ADM. This MUST run before Room.Connect so the SDK wires automatic
    // speaker playout for remote tracks to this ADM — otherwise remote audio is never
    // routed to an output and stays silent. Returns false if the ADM could not be created.
    public bool Initialize()
    {
        if (!InitializePlatformAudio())
            return false;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Remote playout through the ADM starts at room connect regardless of whether
        // the mic is ever published, so the audio session must be set up for the whole
        // controller lifetime.
        SetupAndroidCommunicationAudio();
#endif
        return true;
    }

    // Starts recording and publishes the mic track into the room. Initialize() must have
    // been called (before the room connected) first. On any failure it unpublishes whatever
    // was constructed and leaves IsPublished false; the ADM stays alive so a later Publish
    // can retry.
    public IEnumerator Publish(Room room)
    {
        _room = room;

        if (_platformAudio == null)
        {
            Debug.LogError("[PlatformAudioController] Publish called before Initialize(); aborting.");
            yield break;
        }
        if (IsPublished)
            yield break;

        // No-op when StartCapture already ran at call start (the normal case on
        // Android) or when the capture was kept running across a mute cycle (see
        // Unpublish).
        yield return StartCapture();

#if UNITY_ANDROID && !UNITY_EDITOR
        // Re-assert the preferred route immediately: the route watchdog would pick up
        // any missed device change within its poll interval, but unmuting is a natural
        // point to remove that latency.
        ApplyAndroidCommunicationRoute();
#endif

        _source = new PlatformAudioSource(_platformAudio, _audioOptions);
        _track = LocalAudioTrack.CreateAudioTrack(_trackName, _source, _room);

        Debug.Log($"[PlatformAudioController] Publishing mic track '{_trackName}'...");
        var options = new TrackPublishOptions
        {
            AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
            Source = TrackSource.SourceMicrophone
        };
        var publish = _room.LocalParticipant.PublishTrack(_track, options);
        yield return publish;
        if (publish.IsError)
        {
            Debug.LogError("[PlatformAudioController] Failed to publish microphone track.");
            Unpublish();
            yield break;
        }

        IsPublished = true;
        Debug.Log($"[PlatformAudioController] Microphone track '{_trackName}' published.");
    }

    // Starts the microphone capture without publishing a track; Publish() reuses the
    // running capture. On macOS/iOS this turns on the recording privacy indicator and
    // triggers the OS permission prompt; on Android it awaits the RECORD_AUDIO runtime
    // permission dialog. On Android call this as soon as the call starts, even when
    // joining muted: since Android 13 the app's MODE_IN_COMMUNICATION request — and
    // with it the communication-device pin — is only honored while the app has ACTIVE
    // voice-communication capture or playback, and the ADM's playout stream does not
    // register as active, only the recorder does. Without a running capture the pin is
    // un-owned: it happens to hold in the simple fresh-session case, but after a
    // Bluetooth connect/disconnect episode the platform reasserts the earpiece and
    // wins against the change listener's re-pin.
    public IEnumerator StartCapture()
    {
        if (_platformAudio == null)
        {
            Debug.LogError("[PlatformAudioController] StartCapture called before Initialize(); aborting.");
            yield break;
        }
        if (_isRecording)
            yield break;

        Debug.Log("[PlatformAudioController] Starting platform recording.");
        yield return _platformAudio.StartRecording();
        _isRecording = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        // The pin only became authoritative once the capture went active — re-assert
        // the preferred route in case the platform moved it while the mode was un-owned.
        ApplyAndroidCommunicationRoute();
#endif
    }

    // Tears down the mic capture and track but keeps the ADM alive: remote playout
    // continues and a later Publish() reuses it (e.g. a mute/unmute toggle).
    public void Unpublish()
    {
        IsPublished = false;

        if (_track != null && _room != null)
        {
            Debug.Log("[PlatformAudioController] Unpublishing microphone track.");
            _room.LocalParticipant.UnpublishTrack(_track, stopOnUnpublish: false);
        }
        _track = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Keep the capture stream open while muted. Since Android 13, AudioService only
        // honors this app's MODE_IN_COMMUNICATION request — and with it the
        // communication-device pin — while the app has ACTIVE voice-communication
        // capture or playback: with the recorder stopped, the mode-owner stack reports
        // "Active: false", the mode drops back to MODE_NORMAL, and Telecom re-asserts
        // the earpiece route every ~6 s. The track is unpublished and its source
        // disposed below, so no audio reaches the room, but the OS mic-in-use indicator
        // stays on while muted — same as other conferencing apps. Recording stops in
        // StopCapture (call end) or Dispose.
#else
        StopCapture();
#endif

        _source?.Dispose();
        _source = null;

        // The Android route override is likewise deliberately kept: the ADM continues
        // playing remote audio while the mic is unpublished (listen-only / muted), and
        // clearing the route here would drop that playout back onto the earpiece.
        // Teardown happens in Dispose.
    }

    // Gates call audio on the ADM while the app keeps ownership of the audio session.
    // On iOS this switches WebRTC's VPIO unit on/off so other Unity audio (e.g.
    // background music) survives leaving a room; on other platforms it is a no-op.
    // Call with true after joining a room and false when leaving it.
    public void SetSessionAudioEnabled(bool enabled)
    {
        _platformAudio?.SetSessionAudioEnabled(enabled);
    }

    // Stops the microphone capture if it is running. Only call this once the call has
    // ended (after Unpublish): on Android, stopping the capture while still in a call
    // hands routing authority back to the platform — see StartCapture. The next
    // StartCapture (or Publish) restarts it.
    public void StopCapture()
    {
        if (_platformAudio == null || !_isRecording)
            return;
        try
        {
            _platformAudio.StopRecording();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlatformAudioController] Failed to stop recording: {e.Message}");
        }
        _isRecording = false;
    }

    // Sets up PlatformAudio with the default recording/playout devices.
    bool InitializePlatformAudio()
    {
        try
        {
            _platformAudio = new PlatformAudio();
            Debug.Log(
                $"[PlatformAudioController] PlatformAudio initialized " +
                $"({_platformAudio.RecordingDeviceCount} mic(s), {_platformAudio.PlayoutDeviceCount} speaker(s)).");

            var (recording, playout) = _platformAudio.GetDevices();
            Debug.Log("[PlatformAudioController] Recording devices:");
            foreach (var device in recording)
                Debug.Log($"  [{device.Index}] {device.Name}");

            Debug.Log("[PlatformAudioController] Playout devices:");
            foreach (var device in playout)
                Debug.Log($"  [{device.Index}] {device.Name}");

            if (_platformAudio.RecordingDeviceCount > 0)
                _platformAudio.SetRecordingDevice(0);
            if (_platformAudio.PlayoutDeviceCount > 0)
                _platformAudio.SetPlayoutDevice(0);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PlatformAudioController] Failed to initialize PlatformAudio: {e.Message}");
            _platformAudio?.Dispose();
            _platformAudio = null;
            return false;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // Preference order for the voice-communication output route:
    // Bluetooth > wired headset > built-in loudspeaker. The earpiece (and anything
    // unrecognized) is never picked explicitly — when nothing ranked is available we
    // leave the OS default in place, which on a phone IS the earpiece, so it naturally
    // comes last. Note: Bluetooth devices only show up as communication devices if they
    // support a voice profile (HFP/LE Audio); A2DP-only speakers can't carry call audio
    // on Android and fall through to the loudspeaker.
    static int RouteRank(int deviceType)
    {
        switch (deviceType)
        {
            case 26: // AudioDeviceInfo.TYPE_BLE_HEADSET
            case 27: // AudioDeviceInfo.TYPE_BLE_SPEAKER
            case 7:  // AudioDeviceInfo.TYPE_BLUETOOTH_SCO
            case 23: // AudioDeviceInfo.TYPE_HEARING_AID
                return 0;
            case 3:  // AudioDeviceInfo.TYPE_WIRED_HEADSET
            case 4:  // AudioDeviceInfo.TYPE_WIRED_HEADPHONES
            case 22: // AudioDeviceInfo.TYPE_USB_HEADSET
                return 1;
            case 2:  // AudioDeviceInfo.TYPE_BUILTIN_SPEAKER
                return 2;
            default:
                return int.MaxValue;
        }
    }

    static int AndroidSdkInt()
    {
        using var version = new AndroidJavaClass("android.os.Build$VERSION");
        return version.GetStatic<int>("SDK_INT");
    }

    // Caller owns the returned object (wrap it in `using var`).
    static AndroidJavaObject GetAudioManager()
    {
        using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        return activity.Call<AndroidJavaObject>("getSystemService", "audio");
    }

    // C#-side implementation of the Java callback interface. AndroidJavaProxy can only
    // implement interfaces, which is why this listens for communication-device changes
    // rather than subclassing android.media.AudioDeviceCallback (an abstract class).
    sealed class CommunicationDeviceListener : AndroidJavaProxy
    {
        public CommunicationDeviceListener()
            : base("android.media.AudioManager$OnCommunicationDeviceChangedListener") { }

        // Invoked by Android on the activity's main executor — a JVM-attached thread,
        // but NOT the Unity main thread: keep the body restricted to JNI and Debug.Log.
        public void onCommunicationDeviceChanged(AndroidJavaObject device)
        {
            int type = device != null ? device.Call<int>("getType") : -1;
            Debug.Log($"[PlatformAudioController] Communication device changed (type={type}); re-evaluating route.");
            device?.Dispose();
            ApplyAndroidCommunicationRoute();
        }
    }

    // Enters voice-communication audio mode, applies the preferred output route, and
    // starts watching for route changes — all held until Dispose. Owning
    // MODE_IN_COMMUNICATION is what makes the setCommunicationDevice pin authoritative:
    // without it the platform periodically reasserts its own default route (observed on
    // Pixel 8a: after a Bluetooth session ended, Telecom's CallAudioRouteController
    // flipped playout back to the earpiece every ~6 s, endlessly fighting the re-pin).
    // Side effects while the mode is held: hardware volume keys control the call stream,
    // and Bluetooth audio runs over HFP/SCO (call quality) instead of A2DP — standard
    // for call apps.
    void SetupAndroidCommunicationAudio()
    {
        try
        {
            using var audioManager = GetAudioManager();
            _savedAudioMode = audioManager.Call<int>("getMode");
            audioManager.Call("setMode", 3 /* AudioManager.MODE_IN_COMMUNICATION */);
            Debug.Log($"[PlatformAudioController] Audio mode -> MODE_IN_COMMUNICATION (was {_savedAudioMode}).");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlatformAudioController] Failed to enter communication mode: {e.Message}");
        }

        ApplyAndroidCommunicationRoute();
        RegisterAndroidRouteListener();
    }

    void TeardownAndroidCommunicationAudio()
    {
        // Unregister BEFORE clearing: clearCommunicationDevice fires the change event,
        // and a still-registered listener would immediately re-pin the loudspeaker.
        UnregisterAndroidRouteListener();
        ClearAndroidCommunicationRoute();

        try
        {
            using var audioManager = GetAudioManager();
            audioManager.Call("setMode", _savedAudioMode);
            Debug.Log($"[PlatformAudioController] Audio mode restored ({_savedAudioMode}).");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlatformAudioController] Failed to restore audio mode: {e.Message}");
        }
    }

    // Re-evaluates the route whenever the OS changes the communication device — most
    // importantly when the active device disconnects and playout would otherwise fall
    // back to the earpiece. Registered for the whole session (Initialize until Dispose).
    void RegisterAndroidRouteListener()
    {
        try
        {
            if (AndroidSdkInt() < 31)
                return;

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");
            using var executor = activity.Call<AndroidJavaObject>("getMainExecutor");

            _routeListener = new CommunicationDeviceListener();
            audioManager.Call("addOnCommunicationDeviceChangedListener", executor, _routeListener);
            Debug.Log("[PlatformAudioController] Registered communication device listener.");
        }
        catch (Exception e)
        {
            _routeListener = null;
            Debug.LogWarning($"[PlatformAudioController] Failed to register device listener: {e.Message}");
        }
    }

    void UnregisterAndroidRouteListener()
    {
        if (_routeListener == null)
            return;
        try
        {
            using var audioManager = GetAudioManager();
            audioManager.Call("removeOnCommunicationDeviceChangedListener", _routeListener);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlatformAudioController] Failed to unregister device listener: {e.Message}");
        }
        _routeListener = null;
    }

    // Poll fallback for route changes that fire no communication-device event, run via
    // StartCoroutine for the controller's whole lifetime. Device-verified gap on
    // Pixel 8a (Android 16): when the Bluetooth headset powers off mid-call, SCO drops
    // first and the communication device falls back to the earpiece while the headset
    // is still in getAvailableCommunicationDevices — the listener's re-evaluation at
    // that point still ranks the (dying) headset best. The headset leaves the device
    // list up to ~10 s later WITHOUT another communication-device change (the device
    // stays "earpiece"), so the listener never fires again and playout is stuck on the
    // earpiece. Only a device-list diff catches that transition, and
    // AudioDeviceCallback is an abstract class that AndroidJavaProxy cannot implement,
    // hence polling. Also covers devices ADDED while a pin is active, which equally
    // fires no event.
    public IEnumerator AndroidRouteWatchdog()
    {
        var interval = new WaitForSeconds(1.5f);
        while (IsInitialized)
        {
            if (AndroidRouteNeedsReapply())
            {
                Debug.Log("[PlatformAudioController] Route watchdog detected divergence; re-evaluating.");
                ApplyAndroidCommunicationRoute();
            }
            yield return interval;
        }
    }

    // True when a strictly better-ranked communication device is available than the
    // one currently active — the pinned device vanished and the OS fell back to the
    // earpiece, or a better device appeared without an event. Rank (not id) comparison
    // on purpose: a headset can expose several same-rank entries (BLE + SCO) and which
    // of those the OS activates is its call, not a divergence to correct. Kept
    // separate from ApplyAndroidCommunicationRoute so the quiescent poll stays two
    // JNI queries with no logging.
    static bool AndroidRouteNeedsReapply()
    {
        try
        {
            if (AndroidSdkInt() < 31)
                return false;

            using var audioManager = GetAudioManager();
            using var current = audioManager.Call<AndroidJavaObject>("getCommunicationDevice");
            int currentRank = current != null ? RouteRank(current.Call<int>("getType")) : int.MaxValue;

            using var devices = audioManager.Call<AndroidJavaObject>("getAvailableCommunicationDevices");
            int count = devices.Call<int>("size");
            int bestRank = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                using var device = devices.Call<AndroidJavaObject>("get", i);
                int rank = RouteRank(device.Call<int>("getType"));
                if (rank < bestRank)
                    bestRank = rank;
            }
            return bestRank < currentRank;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlatformAudioController] Route watchdog check failed: {e.Message}");
            return false;
        }
    }

    // On Android the native ADM leaves output routing to the OS (SetPlayoutDevice is a
    // documented no-op there): remote tracks play through a voice-communication stream,
    // whose default route is the earpiece. Pick the best route per RouteRank via
    // AudioManager — setCommunicationDevice on Android 12+ (API 31), where
    // setSpeakerphoneOn is deprecated and unreliable, setSpeakerphoneOn below.
    // The route is session-scoped: applied in Initialize, re-evaluated on each Publish
    // and on every OS communication-device change (see RegisterAndroidRouteListener),
    // and cleared in Dispose. Re-pinning is skipped when the best-ranked device is
    // already active — our own setCommunicationDevice fires the change listener, and
    // the no-op check is what stops that feedback loop. The pin only holds while the
    // app owns MODE_IN_COMMUNICATION — see SetupAndroidCommunicationAudio.
    static void ApplyAndroidCommunicationRoute()
    {
        try
        {
            using var audioManager = GetAudioManager();

            if (AndroidSdkInt() >= 31)
            {
                using var current = audioManager.Call<AndroidJavaObject>("getCommunicationDevice");
                int currentId = current != null ? current.Call<int>("getId") : -1;

                using var devices = audioManager.Call<AndroidJavaObject>("getAvailableCommunicationDevices");
                int count = devices.Call<int>("size");
                AndroidJavaObject best = null;
                int bestRank = int.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    var device = devices.Call<AndroidJavaObject>("get", i);
                    int rank = RouteRank(device.Call<int>("getType"));
                    if (rank < bestRank)
                    {
                        best?.Dispose();
                        best = device;
                        bestRank = rank;
                    }
                    else
                    {
                        device.Dispose();
                    }
                }

                if (best != null)
                {
                    if (best.Call<int>("getId") == currentId)
                    {
                        Debug.Log($"[PlatformAudioController] Best route (type={best.Call<int>("getType")}) already active; skipping re-pin.");
                    }
                    else
                    {
                        bool ok = audioManager.Call<bool>("setCommunicationDevice", best);
                        Debug.Log($"[PlatformAudioController] setCommunicationDevice(type={best.Call<int>("getType")}) -> {ok}");
                    }
                    best.Dispose();
                }
                else
                {
                    Debug.LogWarning("[PlatformAudioController] No ranked communication device available; leaving default route.");
                }
            }
            else
            {
                // Legacy path (pre-API-31). If a Bluetooth or wired output is attached,
                // leave routing to the OS instead of hijacking it with the loudspeaker;
                // proper legacy Bluetooth SCO management (startBluetoothSco) is out of
                // scope for this demo. The AudioManager queries are deprecated but this
                // branch only ever runs on old devices.
                if (audioManager.Call<bool>("isBluetoothA2dpOn")
                    || audioManager.Call<bool>("isBluetoothScoOn")
                    || audioManager.Call<bool>("isWiredHeadsetOn"))
                {
                    Debug.Log("[PlatformAudioController] External output attached; leaving OS routing.");
                    return;
                }
                audioManager.Call("setSpeakerphoneOn", true);
                Debug.Log("[PlatformAudioController] setSpeakerphoneOn(true)");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlatformAudioController] Failed to set communication route: {e.Message}");
        }
    }

    // Hands output routing back to the OS default. Only called from Dispose: the route
    // is session-scoped on purpose (see Unpublish).
    static void ClearAndroidCommunicationRoute()
    {
        try
        {
            using var audioManager = GetAudioManager();
            if (AndroidSdkInt() >= 31)
                audioManager.Call("clearCommunicationDevice");
            else
                audioManager.Call("setSpeakerphoneOn", false);
            Debug.Log("[PlatformAudioController] Restored default audio route.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlatformAudioController] Failed to clear communication route: {e.Message}");
        }
    }
#endif

    public void Dispose()
    {
        Unpublish();
        StopCapture();

        _platformAudio?.Dispose();
        _platformAudio = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        TeardownAndroidCommunicationAudio();
#endif

        _room = null;
    }
}
