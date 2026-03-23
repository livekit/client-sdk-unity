using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LiveKit;
using LiveKit.Proto;
using Google.MaterialDesign.Icons;
using RoomOptions = LiveKit.RoomOptions;
using Application = UnityEngine.Application;

#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Manages a LiveKit room connection with local/remote audio and video tracks.
/// </summary>
public class LivekitSamples : MonoBehaviour
{
    private const string LocalVideoTrackName = "my-video-track";
    private const string LocalAudioTrackName = "my-audio-track";

    private const string MicOnIcon = "e029";
    private const string MicOffIcon = "e02b";
    private const string CamOnIcon = "e04b";
    private const string CamOffIcon = "e04c";

    [Header("LiveKit Connection")]
    [SerializeField] private string url = "ws://localhost:7880";
    [SerializeField] private string token = "YOUR_TOKEN";

    [Header("UI Buttons")]
    [SerializeField] private Button cameraButton;
    [SerializeField] private Button microphoneButton;
    [SerializeField] private Button startCallButton;
    [SerializeField] private Button endCallButton;
    [SerializeField] private Button publishDataButton;

    [Header("Video Layout")]
    [SerializeField] private GridLayoutGroup videoTrackParent;
    [SerializeField] private int frameRate = 30;

    private Room _room;
    private WebCamTexture _webCamTexture;
    private Transform _audioTrackParent;
    private List<Button> _inCallButtons;

    private readonly Dictionary<string, GameObject> _videoDisplayObjects = new();
    private readonly Dictionary<string, ResizeTextureController> _resizeTextureControllers = new();
    private readonly Dictionary<string, GameObject> _audioObjects = new();
    private readonly List<VideoStream> _videoStreams = new();

    private RtcVideoSource _rtcVideoSource;
    private RtcAudioSource _rtcAudioSource;
    private LocalVideoTrack _localVideoTrack;
    private LocalAudioTrack _localAudioTrack;
    private bool _cameraActive;
    private bool _microphoneActive;

    #region Lifecycle

    private void Start()
    {
        startCallButton.onClick.AddListener(OnStartCall);
        endCallButton.onClick.AddListener(OnEndCall);
        cameraButton.onClick.AddListener(OnToggleCamera);
        microphoneButton.onClick.AddListener(OnToggleMicrophone);
        publishDataButton.onClick.AddListener(OnPublishData);

        _inCallButtons = new List<Button> { cameraButton, microphoneButton, endCallButton, publishDataButton };
        _audioTrackParent = new GameObject("AudioTrackParent").transform;
    }

    private void Update()
    {
        foreach (var controller in _resizeTextureControllers.Values)
            controller.Resize();
    }

    private void OnApplicationPause(bool pause)
    {
        if (_webCamTexture == null) return;

        if (pause) _webCamTexture.Pause();
        else _webCamTexture.Play();
    }

    private void OnDestroy()
    {
        _webCamTexture?.Stop();
    }

    #endregion

    #region UI Callbacks

    private void OnStartCall()
    {
        RequestCameraPermissionIfNeeded();

        if (_webCamTexture == null)
            StartCoroutine(OpenCamera());

        StartCoroutine(ConnectToRoom());
    }

    private void OnEndCall()
    {
        _room.Disconnect();
        CleanUpAllTracks();
        _room = null;
        SetUiConnected(false);
    }

    private void OnToggleCamera()
    {
        if (!_cameraActive)
        {
            StartCoroutine(PublishLocalCamera());
            SetButtonIcon(cameraButton, CamOnIcon);
        }
        else
        {
            UnpublishLocalCamera();
            SetButtonIcon(cameraButton, CamOffIcon);
        }
    }

    private void OnToggleMicrophone()
    {
        if (!_microphoneActive)
        {
            StartCoroutine(PublishLocalMicrophone());
            SetButtonIcon(microphoneButton, MicOnIcon);
        }
        else
        {
            UnpublishLocalMicrophone();
            SetButtonIcon(microphoneButton, MicOffIcon);
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

        _room = new Room();
        _room.TrackSubscribed += OnTrackSubscribed;
        _room.TrackUnsubscribed += OnTrackUnsubscribed;
        _room.DataReceived += OnDataReceived;

        var connect = _room.Connect(url, token, new RoomOptions());
        yield return connect;

        if (connect.IsError)
        {
            Debug.LogError("LiveKit connection failed");
            _room = null;
            yield break;
        }

        Debug.Log($"Connected to {_room.Name}");
        SetUiConnected(true);
    }

    #endregion

    #region Remote Tracks

    private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        switch (track)
        {
            case RemoteVideoTrack video: AddRemoteVideoTrack(video); break;
            case RemoteAudioTrack audio: AddRemoteAudioTrack(audio); break;
        }
    }

    private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        switch (track)
        {
            case RemoteVideoTrack video: RemoveRemoteVideoTrack(video.Sid); break;
            case RemoteAudioTrack audio: RemoveRemoteAudioTrack(audio.Sid); break;
        }
    }

    private void AddRemoteVideoTrack(RemoteVideoTrack videoTrack)
    {
        var sid = videoTrack.Sid;
        var imageObject = CreateVideoDisplay(sid, invert: true);

        var image = imageObject.GetComponent<RawImage>();
        var stream = new VideoStream(videoTrack);
        stream.TextureReceived += tex => ApplyTextureToDisplayImage(sid, tex, image);

        _videoDisplayObjects[sid] = imageObject;
        imageObject.transform.SetParent(videoTrackParent.transform, false);

        stream.Start();
        StartCoroutine(stream.Update());
        _videoStreams.Add(stream);
    }

    private void AddRemoteAudioTrack(RemoteAudioTrack audioTrack)
    {
        var audioObject = new GameObject($"AudioTrack: {audioTrack.Sid}");
        audioObject.transform.SetParent(_audioTrackParent);

        var source = audioObject.AddComponent<AudioSource>();
        _ = new AudioStream(audioTrack, source);

        _audioObjects[audioTrack.Sid] = audioObject;
    }

    private void RemoveRemoteVideoTrack(string sid)
    {
        if (_videoDisplayObjects.TryGetValue(sid, out var obj))
        {
            Destroy(obj);
            _videoDisplayObjects.Remove(sid);
        }

        if (_resizeTextureControllers.TryGetValue(sid, out var controller))
        {
            controller.Dispose();
            _resizeTextureControllers.Remove(sid);
        }
    }

    private void RemoveRemoteAudioTrack(string sid)
    {
        if (_audioObjects.TryGetValue(sid, out var obj))
        {
            obj.GetComponent<AudioSource>()?.Stop();
            Destroy(obj);
            _audioObjects.Remove(sid);
        }
    }

    private void OnDataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
    {
        var message = System.Text.Encoding.Default.GetString(data);
        Debug.Log($"DataReceived from {participant.Identity}: {message}");
    }

    #endregion

    #region Local Camera

    private IEnumerator PublishLocalCamera()
    {
        if (_videoDisplayObjects.ContainsKey(LocalVideoTrackName)) yield break;

        var source = new WebCameraSource(_webCamTexture);
        var videoDisplayObject = CreateVideoDisplay("My Camera: " + _webCamTexture.deviceName);
        var displayImage = videoDisplayObject.GetComponent<RawImage>();

        source.TextureReceived += tex => ApplyTextureToDisplayImage(LocalVideoTrackName, tex, displayImage);
        videoDisplayObject.transform.SetParent(videoTrackParent.transform, false);

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

        _cameraActive = true;
        _videoDisplayObjects[LocalVideoTrackName] = videoDisplayObject;
        _rtcVideoSource = source;
        source.Start();
        StartCoroutine(source.Update());
    }

    private void UnpublishLocalCamera()
    {
        DisposeSource(ref _rtcVideoSource);
        RemoveRemoteVideoTrack(LocalVideoTrackName);
        _room.LocalParticipant.UnpublishTrack(_localVideoTrack, false);
        _cameraActive = false;
    }

    #endregion

    #region Local Microphone

    private IEnumerator PublishLocalMicrophone()
    {
        if (_audioObjects.ContainsKey(LocalAudioTrackName)) yield break;

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
        _rtcAudioSource = rtcSource;
        rtcSource.Start();
    }

    private void UnpublishLocalMicrophone()
    {
        DisposeSource(ref _rtcAudioSource);

        if (_audioObjects.TryGetValue(LocalAudioTrackName, out var obj))
        {
            obj.GetComponent<AudioSource>()?.Stop();
            Destroy(obj);
            _audioObjects.Remove(LocalAudioTrackName);
        }

        _room.LocalParticipant.UnpublishTrack(_localAudioTrack, false);
        _microphoneActive = false;
    }

    #endregion

    #region Camera Setup

    private IEnumerator OpenCamera()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.LogError("Camera permission not obtained");
            yield break;
        }

        _webCamTexture?.Stop();

        yield return WaitForCameraDevices();

        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("No camera device available");
            yield break;
        }

        var device = WebCamTexture.devices[0];
        var (width, height) = GetCameraResolution();

        _webCamTexture = new WebCamTexture(device.name, width, height, frameRate)
        {
            wrapMode = TextureWrapMode.Repeat
        };
        _webCamTexture.Play();
    }

    private static IEnumerator WaitForCameraDevices()
    {
        for (int i = 0; i < 300 && WebCamTexture.devices.Length == 0; i++)
            yield return new WaitForEndOfFrame();
    }

    private static (int width, int height) GetCameraResolution()
    {
        return Screen.height > Screen.width
            ? (Screen.height, Screen.width)
            : (Screen.width, Screen.height);
    }

    private static void RequestCameraPermissionIfNeeded()
    {
#if PLATFORM_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            Permission.RequestUserPermission(Permission.Camera);
#endif
    }

    #endregion

    #region Helpers

    private void SetUiConnected(bool connected)
    {
        foreach (var button in _inCallButtons)
            button.interactable = connected;

        startCallButton.interactable = !connected;

        if (!connected)
        {
            SetButtonIcon(microphoneButton, MicOffIcon);
            SetButtonIcon(cameraButton, CamOffIcon);
        }
    }

    private static void SetButtonIcon(Button button, string unicode)
    {
        button.GetComponentInChildren<MaterialIcon>().iconUnicode = unicode;
    }

    private GameObject CreateVideoDisplay(string name, bool invert = false)
    {
        var obj = new GameObject(name);
        var rect = obj.AddComponent<RectTransform>();
        if (invert) rect.rotation = Quaternion.Euler(0, 0, 180);
        obj.AddComponent<RawImage>();
        return obj;
    }

    private void ApplyTextureToDisplayImage(string trackId, Texture tex, RawImage image)
    {
        var controller = new ResizeTextureController(tex, videoTrackParent.cellSize.x, videoTrackParent.cellSize.y);
        image.texture = controller.GetTargetTexture();

        if (_resizeTextureControllers.TryGetValue(trackId, out var old))
        {
            old.Dispose();
            _resizeTextureControllers.Remove(trackId);
        }

        _resizeTextureControllers[trackId] = controller;
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
        DisposeSource(ref _rtcAudioSource);
        DisposeSource(ref _rtcVideoSource);

        foreach (var obj in _audioObjects.Values)
        {
            obj.GetComponent<AudioSource>()?.Stop();
            Destroy(obj);
        }
        _audioObjects.Clear();

        foreach (var obj in _videoDisplayObjects.Values)
        {
            var img = obj.GetComponent<RawImage>();
            if (img != null) { img.texture = null; Destroy(img); }
            Destroy(obj);
        }
        _videoDisplayObjects.Clear();

        foreach (var controller in _resizeTextureControllers.Values)
            controller.Dispose();
        _resizeTextureControllers.Clear();

        foreach (var stream in _videoStreams)
        {
            stream.Stop();
            stream.Dispose();
        }
        _videoStreams.Clear();

        _cameraActive = false;
        _microphoneActive = false;
    }

    #endregion
}