using System;
using System.Collections;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

// Drives the duplex platform audio (WebRTC ADM): captures the default microphone with
// AEC/NS/AGC and publishes it as a LiveKit track, and selects the default playout device
// through which remote tracks are played back automatically. Owns every resource it creates
// and tears them down in dependency order on Dispose.
public sealed class PlatformAudioController : IDisposable
{
    const string MicTrackName = "player-mic";

    PlatformAudio _platformAudio;
    PlatformAudioSource _source;
    LocalAudioTrack _track;
    Room _room;

    public bool IsPublished { get; private set; }

    // Creates the WebRTC ADM. This MUST run before Room.Connect so the SDK wires automatic
    // speaker playout for remote tracks to this ADM — otherwise remote (agent) audio is never
    // routed to an output and stays silent. Returns false if the ADM could not be created.
    public bool Initialize()
    {
        return InitializePlatformAudio();
    }

    // Starts recording and publishes the mic track into the room. Initialize() must have been
    // called (before the room connected) first. On any failure it disposes whatever was
    // constructed and leaves IsPublished false; the caller should tear the rest down.
    public IEnumerator Publish(Room room)
    {
        _room = room;

        if (_platformAudio == null)
        {
            Debug.LogError("[PlatformAudioController] Publish called before Initialize(); aborting.");
            yield break;
        }

        // Begin capturing from the default microphone. On macOS/iOS this turns on the
        // recording privacy indicator and triggers the OS permission prompt; on Android
        // it awaits the RECORD_AUDIO runtime permission dialog.
        Debug.Log("[PlatformAudioController] Starting platform recording.");
        yield return _platformAudio.StartRecording();

        // AudioProcessingOptions.Default enables AEC, noise suppression, auto gain control
        // and prefers hardware processing.
        _source = new PlatformAudioSource(_platformAudio, AudioProcessingOptions.Default);
        _track = LocalAudioTrack.CreateAudioTrack(MicTrackName, _source, _room);

        Debug.Log($"[PlatformAudioController] Publishing mic track '{MicTrackName}'...");
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
            Dispose();
            yield break;
        }

        IsPublished = true;
        Debug.Log("[PlatformAudioController] Microphone track published (AEC enabled).");
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

    public void Dispose()
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

        _platformAudio?.Dispose();
        _platformAudio = null;

        _room = null;
    }
}
