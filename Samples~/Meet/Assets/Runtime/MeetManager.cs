using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LiveKit;
using LiveKit.Proto;
using RoomOptions = LiveKit.RoomOptions;

/// <summary>
/// Manages a LiveKit room connection with local/remote audio and video tracks.
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

    private readonly Dictionary<string, AudioStream> _audioStreams = new();

    private RtcVideoSource _localRtcVideoSource;
    private RtcAudioSource _localRtcAudioSource;
    private LocalVideoTrack _localVideoTrack;
    private LocalAudioTrack _localAudioTrack;
    private bool _cameraActive;
    private bool _microphoneActive;

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
            Debug.LogError($"Failed to fetch connection details: {fetch.Exception?.Message}");
            yield break;
        }

        var details = fetch.Result;

        _room = new Room();
        _room.TrackSubscribed += OnTrackSubscribed;
        _room.TrackUnsubscribed += OnTrackUnsubscribed;
        _room.TrackMuted += OnTrackMuted;
        _room.TrackUnmuted += OnTrackUnmuted;
        _room.ParticipantConnected += OnParticipantConnected;
        _room.ParticipantDisconnected += OnParticipantDisconnected;
        _room.DataReceived += OnDataReceived;

        var connect = _room.Connect(details.ServerUrl, details.ParticipantToken, new RoomOptions());
        yield return connect;

        if (connect.IsError)
        {
            Debug.LogError("LiveKit connection failed");
            _room = null;
            yield break;
        }

        Debug.Log($"Connected to {_room.Name}");
        _localId = _room.LocalParticipant.Identity;
        buttonBar.SetConnected(true);

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
            obj.GetComponent<AudioSource>()?.Stop();
            Destroy(obj);
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

    private void OnParticipantConnected(Participant participant)
        => EnsureParticipantTile(participant.Identity);

    private void OnParticipantDisconnected(Participant participant)
    {
        var owned = new List<string>();
        foreach (var kv in _extraVideoOwners)
            if (kv.Value == participant.Identity) owned.Add(kv.Key);
        foreach (var sid in owned) RemoveExtraVideoTile(sid);

        DestroyParticipantTile(participant.Identity);
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
            Source = TrackSource.SourceCamera
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
        if (_audioObjects.ContainsKey(LocalAudioTrackName)) yield break;

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

        if (publish.IsError) yield break;

        _microphoneActive = true;
        _audioObjects[LocalAudioTrackName] = audioObject;
        _localRtcAudioSource = rtcSource;
        rtcSource.Start();

        if (_participantTiles.TryGetValue(_localId, out var tile))
            tile.SetMicMuted(false);
    }

    private void UnpublishLocalMicrophone()
    {
        DisposeSource(ref _localRtcAudioSource);

        if (_audioObjects.TryGetValue(LocalAudioTrackName, out var obj))
        {
            obj.GetComponent<AudioSource>()?.Stop();
            Destroy(obj);
            _audioObjects.Remove(LocalAudioTrackName);
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

        foreach (var obj in _audioObjects.Values)
        {
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
            if (tile.Image != null) tile.Image.texture = null;
            Destroy(tile.gameObject);
        }
        _participantTiles.Clear();

        foreach (var tile in _extraVideoTiles.Values)
        {
            if (tile.Image != null) tile.Image.texture = null;
            Destroy(tile.gameObject);
        }
        _extraVideoTiles.Clear();
        _extraVideoOwners.Clear();

        _cameraActive = false;
        _microphoneActive = false;
    }

    #endregion
}