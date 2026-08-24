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
// earpiece) and keeps the route pinned across device changes for its whole lifetime.
// This controller only demonstrates the observability side by logging DevicesChanged.
// On Android an active mic capture is what keeps the SDK's route authoritative (since
// Android 13 the OS only honors an app's communication-mode request while it has active
// voice-communication capture), so start the capture with StartCapture when the call
// begins (even when joining muted) — it then stays open across mute cycles until
// StopCapture when the call ends. See StartCapture and Unpublish.
public sealed class PlatformAudioController : IDisposable
{
    readonly string _trackName;
    readonly AudioProcessingOptions _audioOptions;

    PlatformAudio _platformAudio;
    PlatformAudioSource _source;
    LocalAudioTrack _track;
    Room _room;
    bool _isRecording;

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
    static void OnDevicesChanged(IReadOnlyList<AudioDevice> playout, IReadOnlyList<AudioDevice> recording)
    {
        Debug.Log("[PlatformAudioController] Audio devices changed.\n"
            + FormatDeviceLists(playout, recording));
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
            _platformAudio.DevicesChanged -= OnDevicesChanged;
            _platformAudio.Dispose();
            _platformAudio = null;
        }

        _room = null;
    }
}
