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
- [ ] iOS
- [ ] Android
- [ ] WebGL

We plan to support all Unity platforms with this SDK. WebGL is currently supported with [client-sdk-unity-web](https://github.com/livekit/client-sdk-unity-web).

## Installation

Follow this [unity tutorial](https://docs.unity3d.com/Manual/upm-ui-giturl.html) using the `https://github.com/livekit/client-sdk-unity.git` link.
You can then directly import the samples into the package manager.

This repo uses [Git LFS](https://git-lfs.com/), please ensure it's installed when cloning the repo.

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

## Building Native Libraries (macOS)

Both native libraries ship as **universal (fat) binaries** containing x86_64 (Intel) and arm64 (Apple Silicon) slices in a single `.dylib`. This avoids needing separate per-architecture plugin folders in Unity.

### LiveKit FFI (`liblivekit_ffi.dylib`)

Download the prebuilt macOS binaries (`ffi-macos-x86_64.zip` and `ffi-macos-arm64.zip`) from the [livekit/rust-sdks releases](https://github.com/livekit/rust-sdks/releases) page. Look for the latest `livekit-ffi` release.

Extract both zips into `Runtime/Plugins/ffi-macos-x86_64/` and `Runtime/Plugins/ffi-macos-arm64/` respectively, then combine them into a single universal binary:

```bash
mkdir -p Runtime/Plugins/mac_cross

lipo -create \
  -output Runtime/Plugins/mac_cross/liblivekit_ffi.dylib \
  Runtime/Plugins/ffi-macos-x86_64/liblivekit_ffi.dylib \
  Runtime/Plugins/ffi-macos-arm64/liblivekit_ffi.dylib
```

Verify the result:

```bash
lipo -info Runtime/Plugins/mac_cross/liblivekit_ffi.dylib
# Expected: Architectures in the fat file: x86_64 arm64
```

The Unity `.meta` for this plugin must target **Standalone: OSXUniversal** with **CPU: AnyCPU** so Unity loads it on both architectures.

### RustAudio (`librust_audio.dylib`)

A build script is provided at `RustAudio/rust-audio/build.sh`. From the repo root:

```bash
cd RustAudio/rust-audio
./build.sh
```

This builds for both `x86_64-apple-darwin` and `aarch64-apple-darwin`, merges them with `lipo`, and outputs the universal binary to `RustAudio/Wrap/Libraries/librust_audio.dylib`.

**Prerequisites:** Rust toolchain with both targets installed:

```bash
rustup target add x86_64-apple-darwin aarch64-apple-darwin
```

<!--BEGIN_REPO_NAV-->
<!--END_REPO_NAV-->
