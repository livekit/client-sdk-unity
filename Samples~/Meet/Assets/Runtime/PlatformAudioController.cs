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
    // Long enough to swallow the second signal for the same route change: the SDK's
    // Android poll can trail Unity's own notification by up to ~1.5 s.
    const float UnityAudioResetCoalesceSeconds = 2f;

    readonly string _trackName;
    readonly AudioProcessingOptions _audioOptions;

    PlatformAudio _platformAudio;
    PlatformAudioSource _source;
    LocalAudioTrack _track;
    Room _room;
    bool _isRecording;
    string _outputDevices;
    float _lastUnityAudioReset = float.NegativeInfinity;

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
        // back: this controller is created at app start (to keep one ADM alive for every
        // call), while the platform's call audio session should only be held while a call
        // is actually in progress — enabled means "in a call". Without this the app keeps
        // requesting communication mode and pinning the call route from launch to quit.
        // MeetManager re-enables it on join and disables it again on leave.
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
            // Baseline for the recovery: only a change from here on is a device change.
            _outputDevices = OutputDeviceSetKey(playout);

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
    // This sample also uses it to bring Unity's own audio back onto the new route (see
    // ResetUnityAudioOutput).
    void OnDevicesChanged(IReadOnlyList<AudioDevice> playout, IReadOnlyList<AudioDevice> recording)
    {
        Debug.Log("[PlatformAudioController] Audio devices changed.\n"
            + FormatDeviceLists(playout, recording));

        // Recover Unity audio when a device was added or removed — not when the route
        // merely moved between the devices already connected. Device-verified on a
        // Pixel 8a (Android 16): connecting or disconnecting a Bluetooth headset kills
        // game audio, while the route changes a call brings with it (including the
        // platform moving media from the headset's A2DP link onto its call link when the
        // call starts) leave it playing. Resetting on those too would restart the game's
        // audio at every join and hang-up for nothing.
        var devices = OutputDeviceSetKey(playout);
        if (devices == _outputDevices)
            return;
        _outputDevices = devices;
        ResetUnityAudioOutput("output device added or removed");
    }

    // Unity's audio engine opens an output device when the app starts and keeps writing
    // to it: when that device goes away or a new one takes over (Bluetooth connect or
    // disconnect, wired plug/unplug), game audio does not follow and does not recover on
    // its own. Reopening the engine with AudioSettings.Reset is the fix, and it is
    // deliberately the app's call rather than the SDK's — it stops every AudioSource in
    // the scene.
    //
    // Two signals drive it, and both are needed — device-verified on a Pixel 8a
    // (Android 16), where each one alone misses the case the other catches:
    //   - AudioSettings.OnAudioConfigurationChanged, Unity's own notification. It fires
    //     immediately when a Bluetooth headset disconnects, but with
    //     deviceWasChanged=false, so the flag cannot be used to tell a device change from
    //     any other reconfiguration — this sample reacts to the callback either way.
    //   - PlatformAudio.DevicesChanged, the SDK's routing event, as the backstop for
    //     platforms or transitions where Unity stays quiet. It can be the slower of the
    //     two on Bluetooth teardown: a powered-off headset can linger in the platform's
    //     device list for several seconds after the route has already moved.
    // Whichever arrives first triggers the reset; ResetUnityAudioOutput coalesces the
    // other one, along with the callback that AudioSettings.Reset raises itself (it
    // always lands inside the coalescing window, so the recovery cannot feed itself).
    void OnUnityAudioConfigurationChanged(bool deviceWasChanged)
    {
        Debug.Log("[PlatformAudioController] Unity audio configuration changed "
            + $"(deviceWasChanged={deviceWasChanged}, outputSampleRate={AudioSettings.outputSampleRate}, "
            + $"speakerMode={AudioSettings.speakerMode}).");

        ResetUnityAudioOutput("Unity audio configuration change");
    }

    void ResetUnityAudioOutput(string reason)
    {
        // Both callers run on the Unity main thread (the SDK marshals DevicesChanged
        // there), so the Unity APIs below are safe to touch.
        var now = Time.realtimeSinceStartup;
        if (now - _lastUnityAudioReset < UnityAudioResetCoalesceSeconds)
        {
            Debug.Log($"[PlatformAudioController] Unity audio already reopened {now - _lastUnityAudioReset:0.00}s "
                + $"ago, skipping ({reason}).");
            return;
        }
        _lastUnityAudioReset = now;

        // Reset stops every AudioSource, so remember what was playing and where, and
        // pick each one up at the same position on the reopened engine; an app would
        // restart its own music/SFX here instead of sweeping the scene.
        var playing = new List<(AudioSource Source, float Time)>();
        foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            if (source.isPlaying)
                playing.Add((source, source.time));

        Debug.Log($"[PlatformAudioController] Reopening Unity's audio output ({reason}), "
            + $"resuming {playing.Count} source(s).");

        if (!AudioSettings.Reset(AudioSettings.GetConfiguration()))
        {
            Debug.LogWarning("[PlatformAudioController] AudioSettings.Reset failed; Unity audio may stay silent.");
            return;
        }

        var resumed = 0;
        foreach (var (source, time) in playing)
        {
            if (source == null) continue;
            source.Stop();
            source.time = time;
            source.Play();
            if (source.isPlaying) resumed++;
        }

        // Reported so a device run can tell "the engine came back and the sources are
        // running" from "the sources are running but nothing is audible" — the second
        // would mean the reset did not reopen the output the platform actually moved to.
        Debug.Log($"[PlatformAudioController] Unity audio output reopened, {resumed}/{playing.Count} "
            + $"source(s) playing on {AudioSettings.speakerMode} @ {AudioSettings.outputSampleRate} Hz.");
    }

    // Identifies the set of connected output devices, ignoring which one is active and
    // the order the platform enumerated them in.
    static string OutputDeviceSetKey(IReadOnlyList<AudioDevice> playout)
    {
        var keys = new List<string>(playout.Count);
        foreach (var device in playout)
            keys.Add(string.IsNullOrEmpty(device.Guid) ? device.Name : device.Guid);
        keys.Sort(StringComparer.Ordinal);
        return string.Join("|", keys);
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
