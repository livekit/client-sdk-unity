<!--BEGIN_BANNER_IMAGE-->
<!--END_BANNER_IMAGE-->

# LiveKit Unity SDK

⚠️ Warning

> This SDK is currently in Developer Preview mode and not ready for production use. There will be bugs and APIs may change during this period.
>
> We welcome and appreciate any feedback or contributions. You can create issues here or chat live with us in the #rust-developer-preview channel within the [LiveKit Community Slack](https://livekit.io/join-slack).

<!--BEGIN_DESCRIPTION-->Use this SDK to add real-time video, audio and data features to your Unity app. By connecting to a self- or cloud-hosted <a href="https://livekit.io/">LiveKit</a> server, you can quickly build applications like interactive live streaming or video calls with just a few lines of code.<!--END_DESCRIPTION-->

## Platform Support

- [x] Windows
- [x] MacOS
- [x] Linux
- [x] iOS
- [x] Android
- [ ] WebGL

We plan to support all Unity platforms with this SDK. WebGL is currently supported with [client-sdk-unity-web](https://github.com/livekit/client-sdk-unity-web).

## Installation

Follow this [unity tutorial](https://docs.unity3d.com/Manual/upm-ui-giturl.html),

clone this repo and download the ffi binaries.

```sh
git clone https://github.com/livekit/client-sdk-unity.git
cd client-sdk-unity
python3 install.py
```

You can use the package manager to import `client-sdk-unity` into your Unity project.

### iOS

Add dependent frameworks to your Unity project

select `Unity-iPhone` -> `TARGETS` -> `UnityFramework` -> `General` -> `Frameworks and Libraries` -> `+`

add the following frameworks:

`OpenGLES.framework` `MetalKit.framework` `GLKit.framework` `MetalKit.framework` `VideoToolBox.framework` `Network.framework`

add other linker flags to `UnityFramework`:

`-ObjC`

Since `libiPhone-lib.a` has built-in old versions of `celt` and `libvpx` (This will cause the opus and vp8/vp9 codecs to not be called correctly and cause a crash.), so you need to adjust the link order to ensure that it is linked to `liblivekit_ffi.a` first.

The fix is ​​to remove and re-add `libiPhone-lib.a` from `Frameworks and Libraries`, making sure to link after `liblivekit_ffi.a`.

## Examples

And you can also follow the example app in the [unity-example](https://github.com/livekit-examples/unity-example.git) to see how to use the SDK.


### Connect to a room

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
var localSid = "my-audio-source";
GameObject audObject = new GameObject(localSid);
var source = audObject.AddComponent<AudioSource>();
source.clip = Microphone.Start(Microphone.devices[0], true, 2, (int)RtcAudioSource.DefaultSampleRate);
source.loop = true;

var rtcSource = new RtcAudioSource(source);
var track = LocalAudioTrack.CreateAudioTrack("my-audio-track", rtcSource, room);

var options = new TrackPublishOptions();
options.AudioEncoding = new AudioEncoding();
options.AudioEncoding.MaxBitrate = 64000;
options.Source = TrackSource.SourceMicrophone;

var publish = room.LocalParticipant.PublishTrack(track, options);
yield return publish;

if (!publish.IsError)
{
    Debug.Log("Track published!");
}

rtcSource.Start();
```

### Publishing a texture (e.g Unity Camera)

```cs
// publish a WebCamera
//var source = new TextureVideoSource(webCamTexture);

// Publish the entire screen
//var source = new ScreenVideoSource();

// Publishing a Unity Camera
//Camera.main.enabled = true;
//var source = new CameraVideoSource(Camera.main);

var rt = new UnityEngine.RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
rt.Create();
Camera.main.targetTexture = rt;
var source = new TextureVideoSource(rt);
var track = LocalVideoTrack.CreateVideoTrack("my-video-track", source, room);

var options = new TrackPublishOptions();
options.VideoCodec = VideoCodec.Vp8;
var videoCoding = new VideoEncoding();
videoCoding.MaxBitrate = 512000;
videoCoding.MaxFramerate = frameRate;
options.VideoEncoding = videoCoding;
options.Simulcast = true;
options.Source = TrackSource.SourceCamera;

var publish = room.LocalParticipant.PublishTrack(track, options);
yield return publish;

if (!publish.IsError)
{
    Debug.Log("Track published!");
}

source.Start();
StartCoroutine(source.Update());
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
        GameObject audObject = new GameObject(audioTrack.Sid);
        var source = audObject.AddComponent<AudioSource>();
        var stream = new AudioStream(audioTrack, source);
        // Audio is being played on the source ..
    }
}
```

<!--BEGIN_REPO_NAV-->
<!--END_REPO_NAV-->
