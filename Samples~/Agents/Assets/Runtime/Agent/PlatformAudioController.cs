using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

// Drives the duplex platform audio (WebRTC ADM): captures the default microphone with
// the configured audio processing (AEC/NS/AGC) and publishes it as a LiveKit track, and
// selects the default playout device through which remote tracks are played back
// automatically. Publish/Unpublish can be cycled (e.g. a mute toggle) while the ADM stays
// alive; Dispose tears everything down in dependency order.
//
// Output routing is owned by the SDK: PlatformAudio routes to the best available output
// per its ranked OutputPreference (default: Bluetooth > wired headset > speaker >
// earpiece) and keeps the route pinned across device changes while a call is in
// progress. This controller only demonstrates the observability side by logging
// DevicesChanged. On Android an active mic capture is what keeps the SDK's route
// authoritative (since Android 13 the OS only honors an app's communication-mode request
// while it has active voice-communication capture), so start the capture with
// StartCapture when the call begins (even when joining muted) — it then stays open
// across mute cycles until StopCapture when the call ends. See StartCapture and
// Unpublish.
//
// The call audio session itself is gated by SetSessionAudioEnabled: the ADM is created
// once at app start and kept alive, but the platform's call session is only held for the
// duration of a call. See Initialize and SetSessionAudioEnabled.
public sealed class PlatformAudioController : IDisposable
{
    readonly string _trackName;
    readonly AudioProcessingOptions _audioOptions;

    PlatformAudio _platformAudio;
    PlatformAudioSource _source;
    LocalAudioTrack _track;
    Room _room;
    bool _isRecording;
    // What should be audible after an output device change, remembered from before it.
    readonly Dictionary<AudioSource, float> _audibleSources = new Dictionary<AudioSource, float>();

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

        // The SDK routes output automatically from here on; the default
        // PlatformAudio.OutputPreference ranking is already what a call app wants.
        // A custom ranking would be a one-liner:
        //   _platformAudio.OutputPreference = new[] { AudioOutputKind.WiredHeadset, AudioOutputKind.Speaker };
        _platformAudio.DevicesChanged += OnDevicesChanged;
        AudioSettings.OnAudioConfigurationChanged += OnUnityAudioConfigurationChanged;

        // Session audio is enabled when PlatformAudio is created, so hand it straight
        // back: the platform's call audio session should only be held while a call is
        // actually in progress — enabled means "in a call". Without this an app that
        // keeps one ADM alive across calls would request communication mode and pin the
        // call route from launch to quit. The caller must re-enable it when its call
        // starts and disable it again when the call ends (MeetManager does so on
        // join/leave, LiveKitAgentSession around Connect/EndSession).
        _platformAudio.SetSessionAudioEnabled(false);
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
    // joining muted: since Android 13 the app's communication-mode request — and with
    // it the SDK's output route pin — is only honored while the app has ACTIVE
    // voice-communication capture or playback, and the ADM's playout stream does not
    // register as active, only the recorder does. The SDK re-asserts its routing policy
    // whenever the capture (re)starts.
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
        // honors this app's communication-mode request — and with it the SDK's output
        // route pin — while the app has ACTIVE voice-communication capture or playback:
        // with the recorder stopped, the mode drops back to MODE_NORMAL and the platform
        // re-asserts the earpiece route. The track is unpublished and its source
        // disposed below, so no audio reaches the room, but the OS mic-in-use indicator
        // stays on while muted — same as other conferencing apps. Recording stops in
        // StopCapture (call end) or Dispose.
#else
        StopCapture();
#endif

        _source?.Dispose();
        _source = null;
    }

    // Gates the platform's call audio session: enabled means a call is in progress.
    // On iOS it switches WebRTC's VPIO unit on/off while the app keeps ownership of the
    // audio session, on Android 12+ it takes and releases the communication mode plus
    // the SDK's output route pin — both so other Unity audio (e.g. background music)
    // keeps playing outside a call. Call with true after joining a room and false when
    // leaving it; Initialize() already disabled it for the idle app.
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
            Debug.Log(FormatDeviceLists(playout, recording));

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

    // Demonstrates the SDK's routing observability: the routing backend raises
    // DevicesChanged (on the Unity main thread) whenever the available devices or the
    // active route change — headset plugged/unplugged, Bluetooth connected, the route
    // re-pinned after a device disappeared. An app would refresh its device picker here.
    // This sample also uses it as the early warning for the Unity-audio recovery below.
    void OnDevicesChanged(IReadOnlyList<AudioDevice> playout, IReadOnlyList<AudioDevice> recording)
    {
        Debug.Log("[PlatformAudioController] Audio devices changed.\n"
            + FormatDeviceLists(playout, recording));

        // Note what is audible while the engine is still healthy, but do not touch it:
        // this event arrives early in a device switch (device-verified on a Pixel 8a /
        // Android 16, where a Bluetooth headset's call profile appears ~650 ms before the
        // media profile takes over), so reopening the engine here would reopen it onto the
        // output the platform is about to leave.
        RememberAudibleSources();
    }

    // Unity's audio engine opens an output device when the app starts. When that device
    // goes away or another one takes over (Bluetooth connect or disconnect, wired
    // plug/unplug), Unity reinitializes the engine, which stops every AudioSource, and
    // raises this callback afterwards — device-verified on a Pixel 8a (Android 16):
    //
    //   AudioTrack stop(11092): called with 92104 frames delivered   <- sources stopped
    //   [PlatformAudioController] Unity audio configuration changed  <- 25 ms later
    //
    // So the app has to restart its audio here, and it cannot learn what to restart from
    // the scene at this point: everything is already stopped. What should be audible has
    // to be remembered from before the switch (RememberAudibleSources) and put back now.
    // Leaving that out is exactly how game audio ends up silent on the new device.
    //
    // deviceWasChanged is false even for a real device change on Android, so it cannot be
    // used to filter these callbacks; the recovery reacts to all of them and relies on the
    // echo window to ignore the callback its own reset raises.
    void OnUnityAudioConfigurationChanged(bool deviceWasChanged)
    {
        Debug.Log("[PlatformAudioController] Unity audio configuration changed "
            + $"(deviceWasChanged={deviceWasChanged}, outputSampleRate={AudioSettings.outputSampleRate}, "
            + $"speakerMode={AudioSettings.speakerMode}).");

        // Also refreshes the remembered set: anything still playing is the truth for the
        // next switch, and finished one-shots drop out of it.
        RememberAudibleSources();
        RestartAudibleSources();
    }

    // Records what this sample intends to keep audible, so a device change can put it
    // back. Looping sources stay remembered while they are stopped, because that is what
    // a reinitialized engine leaves behind; one-shots are forgotten once they finish. An
    // app would consult its own audio state here instead of sweeping the scene.
    void RememberAudibleSources()
    {
        foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (source.isPlaying)
                _audibleSources[source] = source.time;
            else if (!source.loop)
                _audibleSources.Remove(source);
        }
    }

    // Puts the remembered audio back on the reopened engine. Note what this does NOT do:
    // it never calls AudioSettings.Reset. Unity has already reopened its output by the
    // time it raises the callback, so a reset adds nothing — and on Android it does real
    // harm. Device-verified on a Pixel 8a (Android 16): reinitializing the engine makes
    // Unity claim the headset's call link through the deprecated
    // AudioManager.startBluetoothSco(), which evicts the SDK's setCommunicationDevice pin
    // and leaves the platform's SCO state machine unable to connect —
    //
    //   AS.AudioDeviceBroker: setCommunicationRouteForClient … type:bt_sco addr:
    //       … from API: startBluetoothSco()) from u/pid:…      <- evicts our pinned device
    //   AS.BtHelper: requestScoState: failed to connect in state 1   <- every retry after
    //
    // after which call audio and game audio are both stuck on the loudspeaker for the
    // rest of the session, however often the SDK re-pins the route.
    void RestartAudibleSources()
    {
        // A looping source that was never seen playing is adopted rather than left silent:
        // it can only have been stopped by the engine reinitializing.
        if (_audibleSources.Count == 0)
            foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (source.loop)
                    _audibleSources[source] = 0f;

        var restarted = 0;
        // Copied because the loop drops destroyed sources from the dictionary.
        foreach (var entry in new List<KeyValuePair<AudioSource, float>>(_audibleSources))
        {
            var source = entry.Key;
            if (source == null)
            {
                _audibleSources.Remove(source);
                continue;
            }
            // Idempotent on purpose: a device switch raises several of these callbacks,
            // and anything Unity left running must be left alone.
            if (source.isPlaying) continue;

            if (source.clip != null)
                source.time = Mathf.Clamp(entry.Value, 0f, Mathf.Max(0f, source.clip.length - 0.05f));
            source.Play();
            if (source.isPlaying) restarted++;
        }

        Debug.Log($"[PlatformAudioController] Restarted {restarted} of {_audibleSources.Count} remembered "
            + $"source(s) on {AudioSettings.speakerMode} @ {AudioSettings.outputSampleRate} Hz.");
    }

    static string FormatDeviceLists(IReadOnlyList<AudioDevice> playout, IReadOnlyList<AudioDevice> recording)
    {
        var sb = new StringBuilder("Playout devices:");
        foreach (var device in playout)
        {
            sb.Append($"\n  [{device.Index}] {device.Name} (kind={device.Kind}");
            if (device.IsSelected)
                sb.Append(", selected");
            sb.Append(')');
        }
        sb.Append("\nRecording devices:");
        foreach (var device in recording)
            sb.Append($"\n  [{device.Index}] {device.Name}");
        return sb.ToString();
    }

    public void Dispose()
    {
        Unpublish();
        StopCapture();

        if (_platformAudio != null)
        {
            AudioSettings.OnAudioConfigurationChanged -= OnUnityAudioConfigurationChanged;
            _platformAudio.DevicesChanged -= OnDevicesChanged;
            _platformAudio.Dispose();
            _platformAudio = null;
        }

        _room = null;
    }
}
