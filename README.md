
<!--BEGIN_BANNER_IMAGE-->

<picture>
<source  media="(prefers-color-scheme: dark)"  srcset="/.github/banner_dark.png">
<source  media="(prefers-color-scheme: light)"  srcset="/.github/banner_light.png">
<img  style="width:100%;"  alt="The LiveKit icon, the name of the repository and some sample code in the background."  src="https://raw.githubusercontent.com/livekit/client-sdk-unity/main/.github/banner_light.png">
</picture>
  
<!--END_BANNER_IMAGE-->

# LiveKit Unity SDK

<!--BEGIN_DESCRIPTION-->

Use this SDK to add realtime video, audio and data features to your Unity app. By connecting to <a  href="https://livekit.io/">LiveKit</a> Cloud or a self-hosted server, you can quickly build applications such as multi-modal AI like voice AI agents for NPCs, live streaming, or video calls with just a few lines of code.

<!--END_DESCRIPTION-->

[SDK Reference »](https://livekit.github.io/client-sdk-unity)

## Tutorial

<img  style="width:1000px;"  alt="Screenshot of the LiveKit Unity SDK Youtube tutorial"  src="https://media.githubusercontent.com/media/livekit/client-sdk-unity/refs/heads/max/final-docs-update-for-2.0.0/.github/youtube_tutorial_screenshot.png">

[Youtube Agents NPC Tutorial](https://www.youtube.com/@livekit_io)

## Platform Support

We officially support Unity 2022 onwards, we test on Unity 2022.3.62 and Unity 6000.3.10.

- [x] Windows
- [x] MacOS
- [x] Linux
- [x] iOS
- [x] Android
- [ ] WebGL

We plan to support all Unity platforms with this SDK. WebGL is currently supported with [client-sdk-unity-web](https://github.com/livekit/client-sdk-unity-web).

## Installation

### Using Git

Before cloning the repo, make sure that Git LFS is installed and setup.
You can either clone the repo and import from the local Unity package files:
  
```sh

git  clone  https://github.com/livekit/client-sdk-unity.git

cd  client-sdk-unity

```
  
Or you can import the Git url `https://github.com/livekit/client-sdk-unity.git` from the package manager. 
If you want to use tagged release versions, use `https://github.com/livekit/client-sdk-unity.git#vX.X.X`, for example with `#v1.3.5`.

### Using OpenUPM

The package is also hosted in the OpenUPM package registry. Here is the guide on how to use the OpenUPM registry to import the package: https://openupm.com/packages/io.livekit.livekit-sdk/#modal-manualinstallation 

## Local Development

### Building LiveKit plugins locally

For local development, initialize the Git submodule containing the Rust code for the LiveKit plugin libraries.

There is a [helper script](https://github.com/livekit/client-sdk-unity/blob/main/Scripts~/build_ffi_locally.sh) to build the libraries locally and exchange the downloaded libraries with the local build artifacts in the correct `Runtime/Plugins` folder.

Currently, the build script supports the following arguments:

- macos
- android
- ios

In the following options:

- debug (default)
- release

So a build command is for example:

`./Scripts~/build_ffi_locally.sh macos release` 

### VSCode setup

Look at the Unity-SDK.code-workspace setup for VSCode. This will use the Meet Sample as the Unity project and the Unity SDK package as two roots in a multi-root workspace and the Meet.sln as the `dotnet.defaultSolution`, enabling Rust and C# IDE support.

### Debugging

For C# debugging, there is a simple attach launch option called `C# Unity`, for example in the `Meet/.vscode/launch.json`.

For Rust / C++ debugging on MacOS, you need to install the [CodeLLDB](https://marketplace.visualstudio.com/items?itemName=vadimcn.vscode-lldb) extension. The debug attach is defined in `.vscode/launch.json`.

1. Build the livekit-ffi lib locally in debug mode with `./Scripts~/build_ffi_locally.sh macos debug`
2. Start the Unity Editor
3. Attach to the Unity Editor process (either auto or manual process picker)
4. Start the Scene in Editor

### iOS

Add dependent frameworks to your Unity project

select `Unity-iPhone` -> `TARGETS` -> `UnityFramework` -> `General` -> `Frameworks and Libraries` -> `+`

add the following frameworks:

`OpenGLES.framework`  `MetalKit.framework`  `GLKit.framework`  `MetalKit.framework`  `VideoToolBox.framework`  `Network.framework`

add other linker flags to `UnityFramework`:

`-ObjC`
  
Since `libiPhone-lib.a` has built-in old versions of `celt` and `libvpx` (This will cause the opus and vp8/vp9 codecs to not be called correctly and cause a crash.), you need to ensure that `liblivekit_ffi.a` is linked before `libiPhone-lib.a`. 

The package now applies an iOS post-build fix that rewrites the exported Xcode project so `libiPhone-lib.a` is moved after `liblivekit_ffi.a` in `UnityFramework -> Frameworks and Libraries`.
  
It also strips the old CELT object cluster from the exported `Libraries/libiPhone-lib.a` so Xcode cannot resolve those codec symbols from Unity's archive.

If your project disables package editor scripts or uses a custom Xcode export pipeline that overwrites `project.pbxproj` after LiveKit runs, you may still need to adjust the order manually by removing and re-adding `libiPhone-lib.a`.

## Examples

The repo contains these sample projects:
- [Meet](https://github.com/livekit/client-sdk-unity/tree/main/Samples~/Meet)
- [Agents](https://github.com/livekit/client-sdk-unity/tree/main/Samples~/Agents)

Most of the following functionalities and code snippets can be found in the samples in a similar form to try out.

### Tokens

You need a token to join a LiveKit room as a participant. Read more about tokens here: https://docs.livekit.io/frontends/reference/tokens-grants/

To help getting started with tokens, use `TokenSourceComponent.cs` with a `TokenSourceComponentConfig` ScriptableObject (see https://docs.livekit.io/frontends/build/authentication/#tokensource). Create a config asset via **Right Click > Create > LiveKit > TokenSourceComponentConfig** and select one of three token source types:

#### 1. Literal
Use this to pass a pregenerated server URL and token. Generate tokens via the [LiveKit CLI](https://docs.livekit.io/frontends/build/authentication/custom/#manual-token-creation) or from your [LiveKit Cloud](https://cloud.livekit.io/) project's API key page.

#### 2. Sandbox
For development and testing. Follow the [sandbox token server guide](https://docs.livekit.io/frontends/build/authentication/sandbox-token-server/) to enable your project's sandbox and get the sandbox ID. Optional connection fields (room name, participant name, agent name, etc.) can be configured in the inspector — leave blank for server defaults.

#### 3. Endpoint
For production. Point to your own token endpoint URL and add any required authentication headers. Uses the same connection options as Sandbox. See the [endpoint token generation guide](https://docs.livekit.io/frontends/build/authentication/endpoint/).

#### Usage

Add a `TokenSourceComponent` to a GameObject, assign your `TokenSourceComponentConfig` asset, then fetch connection details before connecting:

```cs
var connectionDetailsTask = _tokenSourceComponent.FetchConnectionDetails();
yield return new WaitUntil(() => connectionDetailsTask.IsCompleted);

if (connectionDetailsTask.IsFaulted)
{
    Debug.LogError($"Failed to fetch connection details: {connectionDetailsTask.Exception?.InnerException?.Message}");
    yield break;
}

var details = connectionDetailsTask.Result;
_room = new Room();
var connect = _room.Connect(details.ServerUrl, details.ParticipantToken, new RoomOptions());
```

Per-call overrides (e.g. dynamic room or participant names) can be passed via `TokenSourceFetchOptions`; any field set there wins over the asset, and unset fields fall back to the config:

```cs
var task = _tokenSourceComponent.FetchConnectionDetails(new TokenSourceFetchOptions
{
    RoomName = "lobby-" + System.Guid.NewGuid(),
    ParticipantName = playerName,
});
```

To skip the ScriptableObject entirely, instantiate a token source directly:

```cs
ITokenSourceFixed source = new TokenSourceLiteral("wss://your.livekit.host", "<join-token>");
// or: new TokenSourceSandbox("<sandbox-id>");
// or: new TokenSourceEndpoint("https://your.token-server/api/token", headers);
// or: new TokenSourceCustom(async () => await MyAuthFlow());
```

### Connecting to a room
  
```cs
IEnumerator ConnectToRoom()
{
    var serverUrl = "< your server url >";
    var token = "< your token >";

    var room = new Room();
    var connect = room.Connect(serverUrl, token, new RoomOptions());

    yield return connect;
}
```

### Video

#### Publishing a texture (e.g Unity Camera)

```cs
IEnumerator PublishCamera(Room room)
{        
    // Option 1: publish a WebCamera
    // var source = new TextureVideoSource(webCamTexture);

    // Option 2: publish a screen share
    // var source = new ScreenVideoSource();

    // Option 3: publishing a Unity Camera
    var source = new CameraVideoSource(Camera.main);
    
    var track = LocalVideoTrack.CreateVideoTrack("my-video-track", source, room);
    
    var videoCoding = new VideoEncoding
    {
        MaxBitrate = 512000,
        MaxFramerate = frameRate
    };

    var options = new TrackPublishOptions
    {
        VideoCodec = VideoCodec.Vp8,
        VideoEncoding = videoCoding,
        Simulcast = true,
        Source = TrackSource.SourceCamera
    };

    var publish = room.LocalParticipant.PublishTrack(track, options);
    yield return publish;

    if (!publish.IsError)
    {
        Debug.Log("Track published!");
    }

    source.Start();
    StartCoroutine(source.Update());
}
```

#### Receiving video

```cs
void OnTrackSubscribed(IRemoteTrack  track, RemoteTrackPublication  publication, RemoteParticipant  participant)
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
    }
}
```

### Audio

There are two options to handle audio input and output, via "Unity Audio" or via "Platform Audio".

#### Unity Audio

Using Unity Audio, the Unity Microphone and AudioSource APIs are used to pipe the audio frames. Use Unity Audio if your game needs to:
- read the audio frames, e.g. for lip syncing
- manipulate the audio frames 

On mobile platforms, the WebRTC audio is handled within the audio session owned by Unity, which reduces complexity.

#### Unity Audio Input

```cs
IEnumerator PublishLocalMicrophoneUnity(Room room)
{
    Debug.Log("Publishing microphone using Unity Audio");
    GameObject microphoneObject = new GameObject("my-audio-source");
    var rtcSource = new MicrophoneSource(Microphone.devices[0], microphoneObject);
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
}
```

#### Unity Audio Output

```cs
void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
{
    if (track is RemoteAudioTrack audioTrack)
    {
        GameObject audioOutputObject = new GameObject(audioTrack.Sid);
        var source = audioOutputObject.AddComponent<AudioSource>();
        var stream = new AudioStream(audioTrack, source);
    }
}
```

#### Platform Audio

With Platform Audio, the audio input and output are managed by the native ADM of WebRTC. This unlocks echo cancellation, noise suppression, auto gain control and hardware processing if available.

There are some known issues with Platform Audio, that we are working on resolving:
- On iOS, disposing of Platform Audio object stops Unity audio output
- On iOS and Unity 6, backgrounding the app breaks Platform Audio
- On MacOS with bluetooth headset, unmuting can break audio output

#### Initialize Platform Audio

Make sure to initialize Platform Audio before connecting to a call.

```cs
void InitializePlatformAudio()
{
    try
    {
        var platformAudio = new PlatformAudio();
        Debug.Log($"PlatformAudio initialized: {platformAudio.RecordingDeviceCount} mics, " +
                    $"{platformAudio.PlayoutDeviceCount} speakers");

        var (recording, playout) = platformAudio.GetDevices();
        Debug.Log("Recording devices:");
        foreach (var device in recording)
            Debug.Log($"  [{device.Index}] {device.Name}");

        Debug.Log("Playout devices:");
        foreach (var device in playout)
            Debug.Log($"  [{device.Index}] {device.Name}");

        if (platformAudio.RecordingDeviceCount > 0)
            platformAudio.SetRecordingDevice(0);
        if (platformAudio.PlayoutDeviceCount > 0)
            platformAudio.SetPlayoutDevice(0);

        Debug.Log($"PlatformAudio ready. AEC={echoCancellation}, NS={noiseSuppression}, AGC={autoGainControl}, HW={preferHardwareProcessing}");
    }
    catch (System.Exception e)
    {
        Debug.LogError($"Failed to initialize PlatformAudio, falling back to Unity audio: {e.Message}");
        usePlatformAudio = false;
        platformAudio = null;
    }
}
```

#### Platform Audio Input

```cs
IEnumerator PublishLocalMicrophonePlatform(PlatformAudio platformAudio, Room room)
{
    if (platformAudio != null)
    {
        yield return platformAudio.StartRecording();
    }

    var audioOptions = new AudioProcessingOptions
    {
        EchoCancellation = echoCancellation,
        NoiseSuppression = noiseSuppression,
        AutoGainControl = autoGainControl,
        PreferHardware = preferHardwareProcessing
    };

    var platformAudioSource = new PlatformAudioSource(platformAudio, audioOptions);
    var localAudioTrack = LocalAudioTrack.CreateAudioTrack(LocalAudioTrackName, platformAudioSource, room);

    var options = new TrackPublishOptions
    {
        AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
        Source = TrackSource.SourceMicrophone
    };

    var publish = room.LocalParticipant.PublishTrack(localAudioTrack, options);
    yield return publish;

    if (publish.IsError)
    {
        Debug.LogError("Failed to publish microphone track");
        platformAudioSource?.Dispose();
        yield break;
    }

    Debug.Log("Microphone published via PlatformAudio (AEC enabled)");
}
```

#### Platform Audio Output

Using Platform Audio, for audio output of subscribed remote audio tracks you don't need any Unity handling. 

### RPC
  
Perform your own predefined method calls from one participant to another.

This feature is especially powerful when used with [Agents](https://docs.livekit.io/agents), for instance to forward LLM function calls to your client application.

The following is a brief overview but [more detail is available in the documentation](https://docs.livekit.io/home/client/data/rpc).

#### Registering an RPC method
  
The participant who implements the method and will receive its calls must first register support. Your method handler will be an async callback that receives an `RpcInvocationData` object:

```cs
void OnRoomConnected(Room room)
{
    room.LocalParticipant.RegisterRpcMethod("greet", HandleGreeting);
}

async Task<string> HandleGreeting(RpcInvocationData data)
{
    Debug.Log($"Received greeting from {data.CallerIdentity}: {data.Payload}");
    return $"Hello, {data.CallerIdentity}!";
}
```

In addition to the payload, `RpcInvocationData` also contains `responseTimeout`, which informs you the maximum time available to return a response. If you are unable to respond in time, the call will result in an error on the caller's side.

#### Performing an RPC request

The caller may initiate an RPC call using coroutines:

```cs
IEnumerator PerformRpcCoroutine(Room room)
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
```

You may find it useful to adjust the `ResponseTimeout` parameter, which indicates the amount of time you will wait for a response. We recommend keeping this value as low as possible while still satisfying the constraints of your application.

#### Errors

LiveKit is a dynamic realtime environment and RPC calls can fail for various reasons.

You may throw errors of the type `RpcError` with a string `message` in an RPC method handler and they will be received on the caller's side with the message intact. Other errors will not be transmitted and will instead arrive to the caller as `1500` ("Application Error"). Other built-in errors are detailed in the [docs](https://docs.livekit.io/home/client/data/rpc/#errors).

### Sending text

Use text streams to send any amount of text between participants.

#### Sending text all at once

```cs
IEnumerator SendText()
{
    var text = "Lorem ipsum dolor sit amet...";
    var sendTextInstruction = room.LocalParticipant.SendText("Hello from Unity", "Chat");
    yield return sendTextInstruction;
}
```

#### Streaming text incrementally

```cs
IEnumerator StreamText(Room room)
{
    var streamWriter = room.LocalParticipant.StreamText("Chat");
    yield return streamWriter;
    string[] textChunks = {"Lorem ", "ipsum ", "dolor ", "sit ", "amet..."};
    foreach (var textChunk in textChunks)
    {
        Debug.Log($"Sending {textChunk}");
        var instruction = streamWriter.Writer.Write(textChunk);
        yield return instruction;
    }
    
    yield return streamWriter.Writer.Close();
}
```

#### Handling incoming streams

```cs
void OnRoomConnected(Room room)
{
    room.RegisterTextStreamHandler("Chat", (reader, identity) => StartCoroutine(OnTextStream(reader, identity)));
}

IEnumerator OnTextStream(TextStreamReader reader, string identity)
{
    // Option 1: Process the stream incrementally
    var readIncremental = reader.ReadIncremental();
    while (true)
    {
        readIncremental.Reset();
        yield return readIncremental;
        if (readIncremental.IsEos)
            break;
        Debug.Log(readIncremental.Text);
    }

    // Option 2: Get the entire text after the stream completes
    var readAllCall = reader.ReadAll();
    yield return readAllCall;
    Debug.Log($"Received text: {readAllCall.Text}");
}
```

### Sending files & bytes

Use byte streams to send files, images, or any other kind of data between participants.

#### Sending files

```cs
IEnumerator SendFile()
{
    var filePath = "path/to/file.jpg";
    Debug.Log($"Sending file {filePath}");
    var sendFileCall = room.LocalParticipant.SendFile(filePath, "my-topic");
    yield return sendFileCall;
}
```

#### Streaming bytes

```cs
IEnumerator StreamBytes()
{
    var streamBytesCall = room.LocalParticipant.StreamBytes("my-topic");
    yield return streamBytesCall;

    var writer = streamBytesCall.Writer;
    Debug.Log($"Opened byte stream with ID: {writer.Info.Id}");

    var dataChunks = new[] 
    {
        new byte[] { 0x00, 0x01 },
        new byte[] { 0x02, 0x03 }
    };

    foreach (var chunk in dataChunks)
    {
        yield return writer.Write(chunk);
    }

    yield return writer.Close();
}
```

#### Handling incoming streams

```cs
void OnRoomConnected(Room room)
{
    room.RegisterByteStreamHandler("my-topic", (reader, identity) => StartCoroutine(HandleByteStream(reader, identity)));
}

IEnumerator HandleByteStream(ByteStreamReader reader, string participantIdentity)
{
    var info = reader.Info;
    
    // Option 1: Process the stream incrementally
    var readIncremental = reader.ReadIncremental();
    while (true)
    {
        readIncremental.Reset();
        yield return readIncremental;
        if (readIncremental.IsEos) break;
        foreach (var dataByte in readIncremental.Bytes)
            Debug.Log($"Received {dataByte}");
    } 

    // Option 2: Get the entire file after the stream completes
    var readAllCall = reader.ReadAll();
    yield return readAllCall;
    var data = readAllCall.Bytes;
    foreach (var dataByte in data)
        Debug.Log($"Received {dataByte}");

    // Option 3: Write the stream to a local file on disk as it arrives
    var writeToFileCall = reader.WriteToFile();
    yield return writeToFileCall;
    var path = writeToFileCall.FilePath;
    Debug.Log($"Wrote to file: {path}");

    Debug.Log($@"
    Byte stream received from {participantIdentity}
    Topic: {info.Topic}
    Timestamp: {info.Timestamp}
    ID: {info.Id}
    Size: {info.TotalLength} (only available if the stream was sent with `SendFile`)
    ");
}
```

## Asynchronous programming: coroutines, async/await, and UniTask

The SDK exposes three interchangeable styles for awaiting asynchronous operations. Coroutines, async/await and UniTask.

**1. Coroutines (default, no dependency)** — shown throughout this README.

**2. async/await (no dependency)** — every operation returns an awaitable instruction (`ConnectInstruction`, `PublishTrackInstruction`, `PerformRpcInstruction`, the stream read instructions, …), so you can `await` it directly. As with coroutines, you inspect success/failure on the instruction (`IsError`) — `await` does not throw. Continuations resume on Unity's main thread.

```cs
async void Start()
{
    var room = new Room();
    var connect = room.Connect("ws://localhost:7880", "<join-token>", new RoomOptions());
    await connect;
    if (!connect.IsError)
        Debug.Log("Connected to " + room.Name);
}
```

> Use `async void` only for top-level event handlers (e.g. button callbacks); its exceptions surface to Unity's log rather than to a caller. Prefer `async Task`/`async UniTaskVoid` elsewhere.

**3. UniTask (optional)** — install [UniTask](https://github.com/Cysharp/UniTask) (`com.cysharp.unitask`). The SDK auto-detects it via the `LIVEKIT_UNITASK` scripting define and enables the `LiveKit.UniTask` assembly, which adds `CancellationToken` support, composition, and async streams.

Cancellation (abandon-awaiter semantics — the underlying request is not cancelled on the wire):

```cs
await room.Connect("ws://localhost:7880", "<join-token>", new RoomOptions())
    .AsUniTask(cancellationToken);
```

Run operations in parallel. `AsUniTask` does not throw on failure (matching the
coroutine path), so keep the instructions and check `IsError` on each after the
`await` — otherwise a failed operation passes silently:

```cs
var publishCamera = room.LocalParticipant.PublishTrack(cameraTrack, cameraOptions);
var publishMicrophone = room.LocalParticipant.PublishTrack(microphoneTrack, microphoneOptions);

await UniTask.WhenAll(publishCamera.AsUniTask(ct), publishMicrophone.AsUniTask(ct));

if (publishCamera.IsError || publishMicrophone.IsError)
    Debug.LogError("Failed to publish one or more tracks");
```

Consume an incremental stream with `await foreach`. The sequence ends at end-of-stream; if the stream ends with an error it throws a `StreamError`:

```cs
try
{
    await foreach (var chunk in reader.ReadIncremental().AsAsyncEnumerable(ct))
        Process(chunk);
}
catch (StreamError e)
{
    Debug.LogError(e.Message);
}
```

> Error-handling differs by API: awaiting an instruction (and `AsUniTask`) never throws on a
> failed operation — you inspect `IsError` after the `await`. The stream enumerable is the
> exception: `await foreach` has no post-loop point to check `IsError`, so a mid-stream failure
> surfaces by throwing `StreamError`.
  
## Verbose Logging

To enable verbose logging, define the `LK_VERBOSE` symbol:

1. Navigate to Project Settings → Player
2. Select your platform tab (e.g., Mac, iOS, Android).
3. Under Other Settings → Scripting Define Symbols, add `LK_VERBOSE`. 

<!--BEGIN_REPO_NAV-->

<br/><table>

<thead><tr><th  colspan="2">LiveKit Ecosystem</th></tr></thead>

<tbody>

<tr><td>Agents SDKs</td><td><a  href="https://github.com/livekit/agents">Python</a> · <a  href="https://github.com/livekit/agents-js">Node.js</a></td></tr><tr></tr>

<tr><td>LiveKit SDKs</td><td><a  href="https://github.com/livekit/client-sdk-js">Browser</a> · <a  href="https://github.com/livekit/client-sdk-swift">Swift</a> · <a  href="https://github.com/livekit/client-sdk-android">Android</a> · <a  href="https://github.com/livekit/client-sdk-flutter">Flutter</a> · <a  href="https://github.com/livekit/client-sdk-react-native">React Native</a> · <a  href="https://github.com/livekit/rust-sdks">Rust</a> · <a  href="https://github.com/livekit/node-sdks">Node.js</a> · <a  href="https://github.com/livekit/python-sdks">Python</a> · <b>Unity</b> · <a  href="https://github.com/livekit/client-sdk-unity-web">Unity (WebGL)</a> · <a  href="https://github.com/livekit/client-sdk-esp32">ESP32</a> · <a  href="https://github.com/livekit/client-sdk-cpp">C++</a></td></tr><tr></tr>

<tr><td>Starter Apps</td><td><a  href="https://github.com/livekit-examples/agent-starter-python">Python Agent</a> · <a  href="https://github.com/livekit-examples/agent-starter-node">TypeScript Agent</a> · <a  href="https://github.com/livekit-examples/agent-starter-react">React App</a> · <a  href="https://github.com/livekit-examples/agent-starter-swift">SwiftUI App</a> · <a  href="https://github.com/livekit-examples/agent-starter-android">Android App</a> · <a  href="https://github.com/livekit-examples/agent-starter-flutter">Flutter App</a> · <a  href="https://github.com/livekit-examples/agent-starter-react-native">React Native App</a> · <a  href="https://github.com/livekit-examples/agent-starter-embed">Web Embed</a></td></tr><tr></tr>

<tr><td>UI Components</td><td><a  href="https://github.com/livekit/components-js">React</a> · <a  href="https://github.com/livekit/components-android">Android Compose</a> · <a  href="https://github.com/livekit/components-swift">SwiftUI</a> · <a  href="https://github.com/livekit/components-flutter">Flutter</a></td></tr><tr></tr>

<tr><td>Server APIs</td><td><a  href="https://github.com/livekit/node-sdks">Node.js</a> · <a  href="https://github.com/livekit/server-sdk-go">Golang</a> · <a  href="https://github.com/livekit/server-sdk-ruby">Ruby</a> · <a  href="https://github.com/livekit/server-sdk-kotlin">Java/Kotlin</a> · <a  href="https://github.com/livekit/python-sdks">Python</a> · <a  href="https://github.com/livekit/rust-sdks">Rust</a> · <a  href="https://github.com/agence104/livekit-server-sdk-php">PHP (community)</a> · <a  href="https://github.com/pabloFuente/livekit-server-sdk-dotnet">.NET (community)</a></td></tr><tr></tr>

<tr><td>Resources</td><td><a  href="https://docs.livekit.io">Docs</a> · <a  href="https://docs.livekit.io/mcp">Docs MCP Server</a> · <a  href="https://github.com/livekit/livekit-cli">CLI</a> · <a  href="https://cloud.livekit.io">LiveKit Cloud</a></td></tr><tr></tr>

<tr><td>LiveKit Server OSS</td><td><a  href="https://github.com/livekit/livekit">LiveKit server</a> · <a  href="https://github.com/livekit/egress">Egress</a> · <a  href="https://github.com/livekit/ingress">Ingress</a> · <a  href="https://github.com/livekit/sip">SIP</a></td></tr><tr></tr>

<tr><td>Community</td><td><a  href="https://community.livekit.io">Developer Community</a> · <a  href="https://livekit.io/join-slack">Slack</a> · <a  href="https://x.com/livekit">X</a> · <a  href="https://www.youtube.com/@livekit_io">YouTube</a></td></tr>

</tbody>

</table>

<!--END_REPO_NAV-->