using System;
using System.Collections;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

// Drives the duplex platform audio (WebRTC ADM): captures the default microphone with
// the configured audio processing (AEC/NS/AGC) and publishes it as a LiveKit track, and
// selects the default playout device through which remote tracks are played back
// automatically. Publish/Unpublish can be cycled (e.g. a mute toggle) while the ADM stays
// alive; Dispose tears everything down in dependency order.
public sealed class PlatformAudioController : IDisposable
{
    readonly string _trackName;
    readonly AudioProcessingOptions _audioOptions;

    PlatformAudio _platformAudio;
    PlatformAudioSource _source;
    LocalAudioTrack _track;
    Room _room;

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
        return InitializePlatformAudio();
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

        // Begin capturing from the default microphone. On macOS/iOS this turns on the
        // recording privacy indicator and triggers the OS permission prompt; on Android
        // it awaits the RECORD_AUDIO runtime permission dialog.
        Debug.Log("[PlatformAudioController] Starting platform recording.");
        yield return _platformAudio.StartRecording();

        // Must run AFTER StartRecording: opening the mic is what flips Android into
        // voice-communication mode and reroutes playback to the earpiece, overriding
        // anything set earlier.
        ApplyAndroidCommunicationRoute(true);

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

        if (_platformAudio != null)
        {
            try
            {
                _platformAudio.StopRecording();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlatformAudioController] Failed to stop recording: {e.Message}");
            }
        }

        _source?.Dispose();
        _source = null;

        // Undo the route override so the OS default applies again outside the capture session.
        ApplyAndroidCommunicationRoute(false);
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
#endif

    // On Android the native ADM leaves output routing to the OS (SetPlayoutDevice is a
    // documented no-op there): once the mic opens, the audio session runs in
    // voice-communication mode, whose default route is the earpiece. Pick the best route
    // per RouteRank via AudioManager — setCommunicationDevice on Android 12+ (API 31),
    // where setSpeakerphoneOn is deprecated and unreliable, setSpeakerphoneOn below.
    // The route is chosen once per Publish() (when the mic opens); devices (dis)connecting
    // mid-conversation are picked up on the next publish.
    static void ApplyAndroidCommunicationRoute(bool enable)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            int sdkInt = version.GetStatic<int>("SDK_INT");

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");

            if (sdkInt >= 31)
            {
                if (enable)
                {
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
                        bool ok = audioManager.Call<bool>("setCommunicationDevice", best);
                        Debug.Log($"[PlatformAudioController] setCommunicationDevice(type={best.Call<int>("getType")}) -> {ok}");
                        best.Dispose();
                    }
                    else
                    {
                        Debug.LogWarning("[PlatformAudioController] No ranked communication device available; leaving default route.");
                    }
                }
                else
                {
                    audioManager.Call("clearCommunicationDevice");
                }
            }
            else
            {
                // Legacy path (pre-API-31). If a Bluetooth or wired output is attached,
                // leave routing to the OS instead of hijacking it with the loudspeaker;
                // proper legacy Bluetooth SCO management (startBluetoothSco) is out of
                // scope for this demo. The AudioManager queries are deprecated but this
                // branch only ever runs on old devices.
                if (enable
                    && (audioManager.Call<bool>("isBluetoothA2dpOn")
                        || audioManager.Call<bool>("isBluetoothScoOn")
                        || audioManager.Call<bool>("isWiredHeadsetOn")))
                {
                    Debug.Log("[PlatformAudioController] External output attached; leaving OS routing.");
                    return;
                }
                audioManager.Call("setSpeakerphoneOn", enable);
                Debug.Log($"[PlatformAudioController] setSpeakerphoneOn({enable})");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlatformAudioController] Failed to set communication route: {e.Message}");
        }
#endif
    }

    public void Dispose()
    {
        Unpublish();

        _platformAudio?.Dispose();
        _platformAudio = null;

        _room = null;
    }
}
