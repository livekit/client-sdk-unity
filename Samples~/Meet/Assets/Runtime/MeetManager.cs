using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LiveKit;
using LiveKit.Proto;
using RoomOptions = LiveKit.RoomOptions;

/// <summary>
/// Manages a LiveKit room connection with local/remote audio and video tracks.
///
/// Supports two audio modes:
/// - PlatformAudio (default): Uses WebRTC's ADM for microphone capture and automatic
///   speaker playout. Provides echo cancellation (AEC), AGC, and noise suppression.
/// - Unity Audio: Uses Unity's Microphone API and AudioStream for manual audio handling.
///   No AEC support but gives more control over audio processing.
/// </summary>
[RequireComponent(typeof(TokenSourceComponent))]
public class MeetManager : MonoBehaviour
{
    private const string LocalVideoTrackName = "my-video-track";
    private const string LocalAudioTrackName = "my-audio-track";

    [Header("UI")]
    [SerializeField] private MeetButtonBar buttonBar;

    [Header("Video Layout")]
    [SerializeField] private GridLayoutGroup videoTrackParent;
    [SerializeField] private ParticipantTile participantTilePrefab;
    [SerializeField] private int frameRate = 30;

    [Header("Audio Mode")]
    [Tooltip("Use PlatformAudio (WebRTC ADM) for microphone capture and automatic speaker playout. " +
             "Provides AEC, AGC, and NS. Disable to use Unity's Microphone API instead.")]
    [SerializeField] private bool usePlatformAudio = true;

    [Header("Audio Processing (PlatformAudio only)")]
    [Tooltip("Enable echo cancellation to remove echo from speaker playback.")]
    [SerializeField] private bool echoCancellation = true;
    [Tooltip("Enable noise suppression to remove background noise.")]
    [SerializeField] private bool noiseSuppression = true;
    [Tooltip("Enable auto gain control to normalize audio levels.")]
    [SerializeField] private bool autoGainControl = true;
    [Tooltip("Prefer hardware audio processing (e.g., iOS VPIO). Lower latency but may have different quality characteristics.")]
    [SerializeField] private bool preferHardwareProcessing = true;

    private const string PlaceholderTextureResourceName = "PlaceholderTileSquare";
    private Texture _placeholderTexture;

    private TokenSourceComponent _tokenSourceComponent;
    private Room _room;
    private string _localId;
    private WebCamTexture _webCamTexture;
    private Transform _audioTrackParent;

    private readonly Dictionary<string, ParticipantTile> _participantTiles = new();
    private readonly Dictionary<string, ParticipantTile> _extraVideoTiles = new(); // e.g. for screen share
    private readonly Dictionary<string, string> _extraVideoOwners = new();
    private readonly Dictionary<string, GameObject> _audioObjects = new();
    private readonly Dictionary<string, VideoStream> _videoStreams = new();
    private readonly HashSet<string> _speakingIdentities = new();

    private readonly Dictionary<string, AudioStream> _audioStreams = new();

    private RtcVideoSource _localRtcVideoSource;
    private RtcAudioSource _localRtcAudioSource;
    private PlatformAudioSource _platformAudioSource;
    private LocalVideoTrack _localVideoTrack;
    private LocalAudioTrack _localAudioTrack;
    private bool _cameraActive;
    private bool _microphoneActive;

    private PlatformAudio _platformAudio;

    #region Lifecycle

    private void Start()
    {
        _tokenSourceComponent = GetComponent<TokenSourceComponent>();

        buttonBar.StartCallClicked += OnStartCall;
        buttonBar.EndCallClicked += OnEndCall;
        buttonBar.ToggleCameraClicked += OnToggleCamera;
        buttonBar.ToggleMicrophoneClicked += OnToggleMicrophone;
        buttonBar.PublishDataClicked += OnPublishData;
        buttonBar.SetConnected(false);

        _audioTrackParent = new GameObject("AudioTrackParent").transform;
        _placeholderTexture = Resources.Load<Texture>(PlaceholderTextureResourceName);

        if (usePlatformAudio)
            InitializePlatformAudio();
    }

    private void InitializePlatformAudio()
    {
        try
        {
            _platformAudio = new PlatformAudio();
            Debug.Log($"PlatformAudio initialized: {_platformAudio.RecordingDeviceCount} mics, " +
                      $"{_platformAudio.PlayoutDeviceCount} speakers");

            var (recording, playout) = _platformAudio.GetDevices();
            Debug.Log("Recording devices:");
            foreach (var device in recording)
                Debug.Log($"  [{device.Index}] {device.Name}");

            Debug.Log("Playout devices:");
            foreach (var device in playout)
                Debug.Log($"  [{device.Index}] {device.Name}");

            if (_platformAudio.RecordingDeviceCount > 0)
                _platformAudio.SetRecordingDevice(0);
            if (_platformAudio.PlayoutDeviceCount > 0)
                _platformAudio.SetPlayoutDevice(0);

            Debug.Log($"PlatformAudio ready. AEC={echoCancellation}, NS={noiseSuppression}, AGC={autoGainControl}, HW={preferHardwareProcessing}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize PlatformAudio, falling back to Unity audio: {e.Message}");
            usePlatformAudio = false;
            _platformAudio = null;
        }
    }

    private void OnApplicationPause(bool pause)
    {
        if (_webCamTexture == null) return;

        if (pause) _webCamTexture.Pause();
        else _webCamTexture.Play();
    }

    private void Update()
    {
        if (!_cameraActive || _webCamTexture == null || _localId == null) return;
        if (_participantTiles.TryGetValue(_localId, out var tile))
            tile.SetLiveRotation(_webCamTexture.videoRotationAngle, _webCamTexture.videoVerticallyMirrored);
    }

    private void OnDestroy()
    {
        // Without this, scene change / app quit while connected leaves all tracks,
        // streams, and their backing GPU/native resources allocated.
        if (_room != null)
        {
            _room.Disconnect();
            _room = null;
        }
        CleanUpAllTracks();
        _webCamTexture?.Stop();
        _platformAudioSource?.Dispose();
        _platformAudio?.Dispose();
        _room?.Disconnect();
    }

    #endregion

    #region UI Callbacks

    private void OnStartCall()
    {
        StartCoroutine(ConnectToRoom());
    }

    private void OnEndCall()
    {
        if (_room == null) return;

        // Disable call audio while keeping the app-owned audio session active, so
        // Unity audio (e.g. background music) survives the hang-up on iOS.
        if (usePlatformAudio)
            _platformAudio?.SetSessionAudioEnabled(false);

        _room.Disconnect();
        CleanUpAllTracks();
        _room = null;
        _localId = null;
        buttonBar.SetConnected(false);
    }

    private void OnToggleCamera()
    {
        if (!_cameraActive)
            StartCoroutine(PublishLocalCamera());
        else
            UnpublishLocalCamera();
    }

    private void OnToggleMicrophone()
    {
        if (!_microphoneActive)
        {
            StartCoroutine(PublishLocalMicrophone());
            buttonBar.SetMicrophoneOn(true);
        }
        else
        {
            UnpublishLocalMicrophone();
            buttonBar.SetMicrophoneOn(false);
        }
    }

    private void OnPublishData()
    {
        Debug.Log($"Published Data");
        var bytes = System.Text.Encoding.Default.GetBytes("hello from unity!");
        _room.LocalParticipant.PublishData(bytes);
        _room.LocalParticipant.SendText("Hello from Unity, Max", "Chat");
    }

    #endregion

    #region Connection

    private IEnumerator ConnectToRoom()
    {
        if (_room != null) yield break;

        var fetch = _tokenSourceComponent.FetchConnectionDetails(new TokenSourceFetchOptions());
        yield return fetch;

        if (fetch.IsError)
        {
            Debug.LogError($"Failed to fetch connection details: {fetch.Exception?.Message} - {fetch.Exception?.InnerException?.Message}");
            yield break;
        }

        var details = fetch.Result;

        _room = new Room();
        _room.TrackSubscribed += OnTrackSubscribed;
        _room.TrackUnsubscribed += OnTrackUnsubscribed;
        _room.TrackMuted += OnTrackMuted;
        _room.TrackUnmuted += OnTrackUnmuted;
        _room.ParticipantConnected += OnParticipantConnected;
        _room.ParticipantDisconnectedWithReason += OnParticipantDisconnected;
        _room.Disconnected += OnDisconnected;
        _room.DataReceived += OnDataReceived;
        _room.ActiveSpeakersChanged += OnActiveSpeakersChanged;

        var connect = _room.Connect(details.ServerUrl, details.ParticipantToken, new RoomOptions());
        yield return connect;

        if (connect.IsError)
        {
            Debug.LogError("LiveKit connection failed");
            _room = null;
            yield break;
        }

        Debug.Log($"Connected to {_room.Name} (PlatformAudio: {usePlatformAudio})");
        _localId = _room.LocalParticipant.Identity;
        buttonBar.SetConnected(true);

        // Enable call audio now that we're in a room. On iOS this turns on WebRTC's
        // VPIO unit while the app keeps ownership of the audio session; leaving the
        // room disables it again (see OnEndCall / OnDisconnected) so other Unity
        // audio keeps playing.
        if (usePlatformAudio)
            _platformAudio?.SetSessionAudioEnabled(true);

        EnsureParticipantTile(_localId);
        foreach (var remote in _room.RemoteParticipants.Values)
            EnsureParticipantTile(remote.Identity);
    }

    #endregion

    #region Remote Tracks

    private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        switch (track)
        {
            case RemoteVideoTrack video when publication.Source == TrackSource.SourceCamera:
                BindRemoteCameraToTile(video, participant.Identity); break;
            case RemoteVideoTrack video:
                AddExtraVideoTile(video, participant.Identity); break;
            case RemoteAudioTrack audio:
                AddRemoteAudioTrack(audio);
                if (publication.Source == TrackSource.SourceMicrophone
                    && _participantTiles.TryGetValue(participant.Identity, out var tile))
                    tile.SetMicMuted(publication.Muted);
                break;
        }
    }

    private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        switch (track)
        {
            case RemoteVideoTrack video when publication.Source == TrackSource.SourceCamera:
                UnbindRemoteCameraFromTile(video.Sid, participant.Identity); break;
            case RemoteVideoTrack video:
                RemoveExtraVideoTile(video.Sid); break;
            case RemoteAudioTrack audio:
                RemoveRemoteAudioTrack(audio.Sid);
                if (publication.Source == TrackSource.SourceMicrophone
                    && _participantTiles.TryGetValue(participant.Identity, out var tile))
                    tile.SetMicMuted(true);
                break;
        }
    }

    private void BindRemoteCameraToTile(RemoteVideoTrack video, string identity)
    {
        EnsureParticipantTile(identity);
        var tile = _participantTiles[identity];
        var sid = video.Sid;

        var stream = new VideoStream(video);
        stream.TextureReceived += tex => tile.BindLiveSource(tex);
        _videoStreams[sid] = stream;
        stream.Start();
        StartCoroutine(stream.Update());
    }

    private void UnbindRemoteCameraFromTile(string sid, string identity)
    {
        if (_videoStreams.TryGetValue(sid, out var stream))
        {
            stream.Stop();
            stream.Dispose();
            _videoStreams.Remove(sid);
        }
        if (_participantTiles.TryGetValue(identity, out var tile))
            tile.ClearLive();
    }

    /// <summary>
    /// If participants have other video sources than their camera (e.g. share their screen).
    /// </summary>
    /// <param name="video">The video track</param>
    /// <param name="identity">The participants identity</param>
    private void AddExtraVideoTile(RemoteVideoTrack video, string identity)
    {
        var sid = video.Sid;
        var tile = Instantiate(participantTilePrefab, videoTrackParent.transform, false);
        tile.gameObject.name = $"Tile: {identity} (screen)";
        if (tile.Label != null) tile.Label.text = $"{identity} (screen)";
        tile.SetPlaceholder(_placeholderTexture);

        var stream = new VideoStream(video);
        stream.TextureReceived += tex => tile.BindLiveSource(tex);

        _extraVideoTiles[sid] = tile;
        _extraVideoOwners[sid] = identity;
        _videoStreams.Add(sid, stream);
        stream.Start();
        StartCoroutine(stream.Update());
    }

    private void RemoveExtraVideoTile(string sid)
    {
        if (_extraVideoTiles.TryGetValue(sid, out var tile))
        {
            Destroy(tile.gameObject);
            _extraVideoTiles.Remove(sid);
        }
        if (_videoStreams.TryGetValue(sid, out var stream))
        {
            stream.Stop();
            stream.Dispose();
            _videoStreams.Remove(sid);
        }
        _extraVideoOwners.Remove(sid);
    }

    private void AddRemoteAudioTrack(RemoteAudioTrack audioTrack)
    {
        var sid = audioTrack.Sid;

        if (usePlatformAudio && _platformAudio != null)
        {
            // PlatformAudio mode: ADM handles speaker playback automatically.
            // No AudioStream / GameObject needed.
            Debug.Log($"Remote audio track {sid} will play via PlatformAudio (automatic)");
            return;
        }

        var audioObject = new GameObject($"AudioTrack: {sid}");
        audioObject.transform.SetParent(_audioTrackParent);

        var source = audioObject.AddComponent<AudioSource>();
        var audiostream = new AudioStream(audioTrack, source);
        _audioStreams.Add(sid, audiostream);

        _audioObjects[sid] = audioObject;
    }

    private void RemoveRemoteAudioTrack(string sid)
    {
        if (_audioObjects.TryGetValue(sid, out var obj))
        {
            if (obj != null) obj.GetComponent<AudioSource>()?.Stop();
            if (obj != null) Destroy(obj);
            _audioObjects.Remove(sid);
        }

        if (_audioStreams.TryGetValue(sid, out var stream))
        {
            stream.Dispose();
            _audioStreams.Remove(sid);
        }
    }

    private void OnDataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
    {
        var message = System.Text.Encoding.Default.GetString(data);
        Debug.Log($"DataReceived from {participant.Identity}: {message}");
    }

    private void OnActiveSpeakersChanged(List<Participant> speakers)
    {
        // The SDK delivers the complete set of currently-speaking participants each
        // time, so anyone previously speaking but absent here has stopped.
        foreach (var identity in _speakingIdentities)
            if (_participantTiles.TryGetValue(identity, out var tile))
                tile.SetSpeaking(false);

        _speakingIdentities.Clear();
        foreach (var participant in speakers)
        {
            _speakingIdentities.Add(participant.Identity);
            if (_participantTiles.TryGetValue(participant.Identity, out var tile))
                tile.SetSpeaking(true);
        }
    }

    private void OnParticipantConnected(Participant participant)
        => EnsureParticipantTile(participant.Identity);

    private void OnParticipantDisconnected(Participant participant, DisconnectReason reason)
    {
        Debug.Log($"Participant {participant.Identity} disconnected: {reason}");

        var owned = new List<string>();
        foreach (var kv in _extraVideoOwners)
            if (kv.Value == participant.Identity) owned.Add(kv.Key);
        foreach (var sid in owned) RemoveExtraVideoTile(sid);

        DestroyParticipantTile(participant.Identity);
    }

    private void OnDisconnected(Room room)
    {
        Debug.Log($"Disconnected from room: {room.DisconnectReason}");

        // Covers server-initiated disconnects as well as OnEndCall; idempotent with
        // the call already made there. Keeps the audio session active for Unity.
        if (usePlatformAudio)
            _platformAudio?.SetSessionAudioEnabled(false);
    }

    private void OnTrackMuted(TrackPublication publication, Participant participant)
    {
        if (publication.Kind == TrackKind.KindAudio
            && publication.Source == TrackSource.SourceMicrophone)
        {
            if (_participantTiles.TryGetValue(participant.Identity, out var tile))
                tile.SetMicMuted(true);
            return;
        }

        if (publication.Kind != TrackKind.KindVideo) return;

        if (publication.Source == TrackSource.SourceCamera)
        {
            if (_participantTiles.TryGetValue(participant.Identity, out var tile))
                tile.ShowPlaceholder();
        }
        else if (_extraVideoTiles.TryGetValue(publication.Sid, out var tile))
        {
            tile.gameObject.SetActive(false);
        }
    }

    private void OnTrackUnmuted(TrackPublication publication, Participant participant)
    {
        if (publication.Kind == TrackKind.KindAudio
            && publication.Source == TrackSource.SourceMicrophone)
        {
            if (_participantTiles.TryGetValue(participant.Identity, out var tile))
                tile.SetMicMuted(false);
            return;
        }

        if (publication.Kind != TrackKind.KindVideo) return;

        if (publication.Source == TrackSource.SourceCamera)
        {
            if (_participantTiles.TryGetValue(participant.Identity, out var tile))
                tile.ShowLive();
        }
        else if (_extraVideoTiles.TryGetValue(publication.Sid, out var tile))
        {
            tile.gameObject.SetActive(true);
        }
    }

    #endregion

    #region Local Camera

    private IEnumerator PublishLocalCamera()
    {
        if (_cameraActive) yield break;

        if (_webCamTexture == null)
            yield return CameraDeviceProvider.Open(frameRate, t => _webCamTexture = t);

        if (_webCamTexture == null) yield break;

        EnsureParticipantTile(_localId);
        var tile = _participantTiles[_localId];

        var source = new WebCameraSource(_webCamTexture);
        _localVideoTrack = LocalVideoTrack.CreateVideoTrack(LocalVideoTrackName, source, _room);

        var options = new TrackPublishOptions
        {
            VideoCodec = VideoCodec.H265,
            VideoEncoding = new VideoEncoding { MaxBitrate = 512000, MaxFramerate = frameRate },
            Simulcast = false,
            Source = TrackSource.SourceCamera,
            DegradationPreference = DegradationPreference.Balanced
        };

        var publish = _room.LocalParticipant.PublishTrack(_localVideoTrack, options);
        yield return publish;

        if (publish.IsError) yield break;

        source.TextureReceived += tex =>
        {
            if (_webCamTexture == null) { tile.BindLiveSource(tex); return; }
            tile.BindLiveSource(tex, _webCamTexture.videoRotationAngle, _webCamTexture.videoVerticallyMirrored);
        };

        _cameraActive = true;
        _localRtcVideoSource = source;
        source.Start();
        StartCoroutine(source.Update());

        buttonBar.SetCameraOn(true);
    }

    private void UnpublishLocalCamera()
    {
        DisposeSource(ref _localRtcVideoSource);

        _room.LocalParticipant.UnpublishTrack(_localVideoTrack, false);
        _localVideoTrack = null;
        if (_participantTiles.TryGetValue(_localId, out var tile))
            tile.ClearLive();
        _cameraActive = false;

        buttonBar.SetCameraOn(false);
    }

    #endregion

    #region Local Microphone

    private IEnumerator PublishLocalMicrophone()
    {
        if (_microphoneActive) yield break;

        if (usePlatformAudio && _platformAudio != null)
            yield return PublishLocalMicrophonePlatform();
        else
            yield return PublishLocalMicrophoneUnity();

        if (_microphoneActive && _participantTiles.TryGetValue(_localId, out var tile))
            tile.SetMicMuted(false);
    }

    private IEnumerator PublishLocalMicrophonePlatform()
    {
        Debug.Log("Publishing microphone using PlatformAudio (ADM)");

        // Start recording (in case it was stopped by a previous mute).
        // This turns on the privacy indicator on macOS/iOS. On Android this also
        // awaits the RECORD_AUDIO runtime permission dialog if not yet granted.
        if (_platformAudio != null)
        {
            yield return _platformAudio.StartRecording();
        }

        var audioOptions = new AudioProcessingOptions
        {
            EchoCancellation = echoCancellation,
            NoiseSuppression = noiseSuppression,
            AutoGainControl = autoGainControl,
            PreferHardware = preferHardwareProcessing
        };

        _platformAudioSource = new PlatformAudioSource(_platformAudio, audioOptions);
        _localAudioTrack = LocalAudioTrack.CreateAudioTrack(LocalAudioTrackName, _platformAudioSource, _room);

        var options = new TrackPublishOptions
        {
            AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
            Source = TrackSource.SourceMicrophone
        };

        var publish = _room.LocalParticipant.PublishTrack(_localAudioTrack, options);
        yield return publish;

        if (publish.IsError)
        {
            Debug.LogError("Failed to publish microphone track");
            _platformAudioSource?.Dispose();
            _platformAudioSource = null;
            _localAudioTrack = null;
            yield break;
        }

        _microphoneActive = true;
        Debug.Log("Microphone published via PlatformAudio (AEC enabled)");
    }

    private IEnumerator PublishLocalMicrophoneUnity()
    {
        Debug.Log("Publishing microphone using Unity Microphone API");

        // Start the microphone here for early iOS permission request and android getting access to Microphone.devices
        Microphone.Start(null, true, 10, 44100);
        
        var audioObject = new GameObject($"My Microphone: {Microphone.devices[0]}");
        audioObject.transform.SetParent(_audioTrackParent);

        var rtcSource = new MicrophoneSource(Microphone.devices[0], audioObject);

        _localAudioTrack = LocalAudioTrack.CreateAudioTrack(LocalAudioTrackName, rtcSource, _room);

        var options = new TrackPublishOptions
        {
            AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
            Source = TrackSource.SourceMicrophone
        };

        var publish = _room.LocalParticipant.PublishTrack(_localAudioTrack, options);
        yield return publish;

        if (publish.IsError)
        {
            Destroy(audioObject);
            _localAudioTrack = null;
            yield break;
        }

        _microphoneActive = true;
        _audioObjects[LocalAudioTrackName] = audioObject;
        _localRtcAudioSource = rtcSource;
        rtcSource.Start();

        Debug.Log("Microphone published via Unity Microphone API (no AEC)");
    }

    private void UnpublishLocalMicrophone()
    {
        if (usePlatformAudio && _platformAudioSource != null)
        {
            try
            {
                _platformAudio?.StopRecording();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to stop recording: {e.Message}");
            }

            _platformAudioSource.Dispose();
            _platformAudioSource = null;
        }
        else
        {
            DisposeSource(ref _localRtcAudioSource);

            if (_audioObjects.TryGetValue(LocalAudioTrackName, out var obj))
            {
                if (obj != null)
                {
                    obj.GetComponent<AudioSource>()?.Stop();
                    Destroy(obj);
                }
                _audioObjects.Remove(LocalAudioTrackName);
            }
        }

        _room.LocalParticipant.UnpublishTrack(_localAudioTrack, false);
        _localAudioTrack = null;
        if (_participantTiles.TryGetValue(_localId, out var tile))
            tile.SetMicMuted(true);
        _microphoneActive = false;
    }

    #endregion

    #region Helpers

    private void EnsureParticipantTile(string identity)
    {
        if (_participantTiles.ContainsKey(identity)) return;

        var tile = Instantiate(participantTilePrefab, videoTrackParent.transform, false);
        tile.gameObject.name = $"Tile: {identity}";
        if (tile.Label != null) tile.Label.text = identity;
        tile.SetPlaceholder(_placeholderTexture);
        if (identity == _localId) tile.transform.SetSiblingIndex(0);
        _participantTiles[identity] = tile;

        SyncMicState(identity);
    }

    private void SyncMicState(string identity)
    {
        if (!_participantTiles.TryGetValue(identity, out var tile)) return;
        if (_room == null) return;

        Participant participant = null;
        if (_room.LocalParticipant != null && _room.LocalParticipant.Identity == identity)
            participant = _room.LocalParticipant;
        else if (_room.RemoteParticipants.TryGetValue(identity, out var rp))
            participant = rp;
        if (participant == null) return;

        bool muted = true;
        foreach (var pub in participant.Tracks.Values)
        {
            if (pub.Kind == TrackKind.KindAudio && pub.Source == TrackSource.SourceMicrophone)
            {
                muted = pub.Muted;
                break;
            }
        }
        tile.SetMicMuted(muted);
    }

    private void DestroyParticipantTile(string identity)
    {
        if (_participantTiles.TryGetValue(identity, out var tile))
        {
            Destroy(tile.gameObject);
            _participantTiles.Remove(identity);
        }
    }

    private static void DisposeSource<T>(ref T source) where T : class, System.IDisposable
    {
        if (source is RtcAudioSource audio) audio.Stop();
        if (source is RtcVideoSource video) video.Stop();
        source?.Dispose();
        source = null;
    }

    private void CleanUpAllTracks()
    {
        DisposeSource(ref _localRtcAudioSource);
        DisposeSource(ref _localRtcVideoSource);

        _platformAudioSource?.Dispose();
        _platformAudioSource = null;

        foreach (var obj in _audioObjects.Values)
        {
            if (obj == null) continue;
            obj.GetComponent<AudioSource>()?.Stop();
            Destroy(obj);
        }
        _audioObjects.Clear();

        foreach (var stream in _audioStreams.Values)
        {
            stream.Dispose();
        }
        _audioStreams.Clear();

        foreach (var stream in _videoStreams.Values)
        {
            stream.Stop();
            stream.Dispose();
        }
        _videoStreams.Clear();

        foreach (var tile in _participantTiles.Values)
        {
            if (tile == null) continue;
            if (tile.Image != null) tile.Image.texture = null;
            Destroy(tile.gameObject);
        }
        _participantTiles.Clear();

        foreach (var tile in _extraVideoTiles.Values)
        {
            if (tile == null) continue;
            if (tile.Image != null) tile.Image.texture = null;
            Destroy(tile.gameObject);
        }
        _extraVideoTiles.Clear();
        _extraVideoOwners.Clear();

        _speakingIdentities.Clear();

        _cameraActive = false;
        _microphoneActive = false;
    }

    #endregion
}
