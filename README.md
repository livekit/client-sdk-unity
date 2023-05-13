# client-sdk-unity

## Installation :
Follow this [unity tutorial](https://docs.unity3d.com/Manual/upm-ui-giturl.html) using the `https://github.com/livekit/client-sdk-unity.git` link.
You can then directly import the samples into the package manager.

## Examples

### Connect to a room:
```cs 
using LiveKit;

IEnumerator Start()
{
    var room = new Room();
    room.TrackSubscribed += TrackSubscribed;

    var connect = room.Connect("ws://localhost:7880", "<join-token>");
    yield return connect;
    if (!connect.IsError) 
    {
        Debug.Log("Connected to " + room.Name);
    }
}
```

### Publishing microphone 
```cs 
// Publish Microphone
var source = GetComponent<AudioSource>();
source.clip = Microphone.Start("MacBook Pro Microphone", true, 2, 48000);
source.loop = true;
Ssurce.Play();

var rtcSource = new RtcAudioSource(Source);
var track = LocalAudioTrack.CreateAudioTrack("my-track", rtcSource);

var options = new TrackPublishOptions();
options.Source = TrackSource.SourceMicrophone;

var publish = room.LocalParticipant.PublishTrack(track, options);
yield return publish;

if (!publish.IsError) 
{
    Debug.Log("Track published!");
}
```

### Publishing a texture (e.g Unity Camera)
```cs
var rt = new UnityEngine.RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
rt.Create();
Camera.main.targetTexture = rt;

var source = new TextureVideoSource(rt);
var track = LocalVideoTrack.CreateVideoTrack("my-track", source);

var options = new TrackPublishOptions();
options.VideoCodec = VideoCodec.H264;
options.Source = TrackSource.SourceCamera;

var publish = _room.LocalParticipant.PublishTrack(track, options);
yield return publish;

if (!publish.IsError) 
{
    Debug.Log("Track published!");
}
```

### Receiving tracks 
```cs
IEnumerator Start()
{
    var room = new Room();
    room.TrackSubscribed += TrackSubscribed;

    var connect = room.Connect("ws://localhost:7880", "<join-token>");
    yield return connect;

    // ....
}

void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
{
    if (track is RemoteVideoTrack videoTrack)
    {
        var rawImage = GetComponent<RawImage>();
        var stream = new VideoStream(videoTrack);
        stream.TextureReceived += (tex) =>
        {
            rawImage.texture = tex;
        };
        StartCoroutine(stream.Update());
        // The video data is displayed on the rawImage
    }
    else if (track is RemoteAudioTrack audioTrack)
    {
        var source = GetComponent<AudioSource>();
        var stream = new AudioStream(audioTrack, source);
        // Audio is being played on the source ..
    }
}
```
