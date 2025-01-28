<!--BEGIN_BANNER_IMAGE-->

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="/.github/banner_dark.png">
  <source media="(prefers-color-scheme: light)" srcset="/.github/banner_light.png">
  <img style="width:100%;" alt="The LiveKit icon, the name of the repository and some sample code in the background." src="https://raw.githubusercontent.com/livekit/client-sdk-unity/main/.github/banner_light.png">
</picture>

<!--END_BANNER_IMAGE-->

# LiveKit Unity SDK

⚠️ Warning

> This SDK is currently in Developer Preview mode and not ready for production use. There will be bugs and APIs may change during this period.
>
> We welcome and appreciate any feedback or contributions. You can create issues here or chat live with us in the #rust-developer-preview channel within the [LiveKit Community Slack](https://livekit.io/join-slack).

<!--BEGIN_DESCRIPTION-->
Use this SDK to add realtime video, audio and data features to your Unity app. By connecting to <a href="https://livekit.io/">LiveKit</a> Cloud or a self-hosted server, you can quickly build applications such as multi-modal AI, live streaming, or video calls with just a few lines of code.
<!--END_DESCRIPTION-->

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
```

You can use the package manager to import local `client-sdk-unity` into your Unity project.

Or you can import the git url `https://github.com/livekit/client-sdk-unity.git` from the package manager.



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

var rtcSource = new RtcAudioSource(source, RtcAudioSourceType.AudioSourceMicrophone);
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

### RPC

Perform your own predefined method calls from one participant to another. 

This feature is especially powerful when used with [Agents](https://docs.livekit.io/agents), for instance to forward LLM function calls to your client application.

The following is a brief overview but [more detail is available in the documentation](https://docs.livekit.io/home/client/data/rpc).

#### Registering an RPC method

The participant who implements the method and will receive its calls must first register support. Your method handler will be an async callback that receives an `RpcInvocationData` object:

```cs
// Define your method handler
async Task<string> HandleGreeting(RpcInvocationData data)
{
    Debug.Log($"Received greeting from {data.CallerIdentity}: {data.Payload}");
    return $"Hello, {data.CallerIdentity}!";
}

// Register the method after connection to the room
room.LocalParticipant.RegisterRpcMethod("greet", HandleGreeting);
```

In addition to the payload, `RpcInvocationData` also contains `responseTimeout`, which informs you the maximum time available to return a response. If you are unable to respond in time, the call will result in an error on the caller's side.

#### Performing an RPC request

The caller may initiate an RPC call using coroutines:

```cs
IEnumerator PerformRpcCoroutine()
{
    var rpcCall = room.LocalParticipant.PerformRpc(new PerformRpcParams
    {
        DestinationIdentity = "recipient-identity",
        Method = "greet",
        Payload = "Hello from RPC!"
    });
    
    yield return rpcCall;

    if (rpcCall.IsError)
    {
        Debug.Log($"RPC call failed: {rpcCall.Error}");
    }
    else
    {
        Debug.Log($"RPC response: {rpcCall.Payload}");
    }
}

// Start the coroutine from another MonoBehaviour method
StartCoroutine(PerformRpcCoroutine());
```

You may find it useful to adjust the `ResponseTimeout` parameter, which indicates the amount of time you will wait for a response. We recommend keeping this value as low as possible while still satisfying the constraints of your application.

#### Errors

LiveKit is a dynamic realtime environment and RPC calls can fail for various reasons. 

You may throw errors of the type `RpcError` with a string `message` in an RPC method handler and they will be received on the caller's side with the message intact. Other errors will not be transmitted and will instead arrive to the caller as `1500` ("Application Error"). Other built-in errors are detailed in the [docs](https://docs.livekit.io/home/client/data/rpc/#errors).



<!--BEGIN_REPO_NAV-->
<br/><table>
<thead><tr><th colspan="2">LiveKit Ecosystem</th></tr></thead>
<tbody>
<tr><td>LiveKit SDKs</td><td><a href="https://github.com/livekit/client-sdk-js">Browser</a> · <a href="https://github.com/livekit/client-sdk-swift">iOS/macOS/visionOS</a> · <a href="https://github.com/livekit/client-sdk-android">Android</a> · <a href="https://github.com/livekit/client-sdk-flutter">Flutter</a> · <a href="https://github.com/livekit/client-sdk-react-native">React Native</a> · <a href="https://github.com/livekit/rust-sdks">Rust</a> · <a href="https://github.com/livekit/node-sdks">Node.js</a> · <a href="https://github.com/livekit/python-sdks">Python</a> · <b>Unity</b> · <a href="https://github.com/livekit/client-sdk-unity-web">Unity (WebGL)</a></td></tr><tr></tr>
<tr><td>Server APIs</td><td><a href="https://github.com/livekit/node-sdks">Node.js</a> · <a href="https://github.com/livekit/server-sdk-go">Golang</a> · <a href="https://github.com/livekit/server-sdk-ruby">Ruby</a> · <a href="https://github.com/livekit/server-sdk-kotlin">Java/Kotlin</a> · <a href="https://github.com/livekit/python-sdks">Python</a> · <a href="https://github.com/livekit/rust-sdks">Rust</a> · <a href="https://github.com/agence104/livekit-server-sdk-php">PHP (community)</a> · <a href="https://github.com/pabloFuente/livekit-server-sdk-dotnet">.NET (community)</a></td></tr><tr></tr>
<tr><td>UI Components</td><td><a href="https://github.com/livekit/components-js">React</a> · <a href="https://github.com/livekit/components-android">Android Compose</a> · <a href="https://github.com/livekit/components-swift">SwiftUI</a></td></tr><tr></tr>
<tr><td>Agents Frameworks</td><td><a href="https://github.com/livekit/agents">Python</a> · <a href="https://github.com/livekit/agents-js">Node.js</a> · <a href="https://github.com/livekit/agent-playground">Playground</a></td></tr><tr></tr>
<tr><td>Services</td><td><a href="https://github.com/livekit/livekit">LiveKit server</a> · <a href="https://github.com/livekit/egress">Egress</a> · <a href="https://github.com/livekit/ingress">Ingress</a> · <a href="https://github.com/livekit/sip">SIP</a></td></tr><tr></tr>
<tr><td>Resources</td><td><a href="https://docs.livekit.io">Docs</a> · <a href="https://github.com/livekit-examples">Example apps</a> · <a href="https://livekit.io/cloud">Cloud</a> · <a href="https://docs.livekit.io/home/self-hosting/deployment">Self-hosting</a> · <a href="https://github.com/livekit/livekit-cli">CLI</a></td></tr>
</tbody>
</table>
<!--END_REPO_NAV-->
