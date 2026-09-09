# AGENTS.md

This file provides guidance to AI coding agents (e.g. Claude Code) when working with code in this repository. `CLAUDE.md` includes it via `@AGENTS.md`.

## Project Overview

LiveKit Unity SDK — a C# wrapper around LiveKit's Rust SDK using FFI (Foreign Function Interface) for real-time audio/video communication. Unity package name: `io.livekit.livekit-sdk`.

## Build & Development Commands

### Run Unity tests and builds locally
```bash
Scripts~/run_unity.sh test [-f FILTER] [-m EditMode|PlayMode|both] [-n N]
Scripts~/run_unity.sh build <platform>
```
- `UNITY_PATH` overrides the Unity binary (auto-detect picks the *oldest* editor installed via Unity Hub, which may not match the project — e.g. `Samples~/Meet` targets Unity 6)
- `PROJECT_PATH` overrides the Unity project (default: `Samples~/Meet`)
- Test results go to `Logs~/` by default

### Build FFI locally from Rust source
```bash
# Requires the client-sdk-rust~ submodule and Rust toolchain
Scripts~/build_ffi_locally.sh <platform> [build_type]
# Platforms: macos, android, ios
# Build types: debug (default), release
```
- **macOS**: requires `aarch64-apple-darwin` target
- **Android**: requires `cargo-ndk` and Android NDK
- **iOS**: builds static lib (`liblivekit_ffi.a`)
- After macOS builds, Unity must be restarted to load the new dylib
- macOS builds also run `Scripts~/generate_uniffi_bindings.sh` on the new dylib (see below); if it carries no UniFFI metadata the step warns and leaves the existing bindings untouched

### Build the UniFFI library locally from Rust source
```bash
# Builds the experimental livekit-uniffi crate (client-sdk-rust~/livekit-uniffi), macOS only
Scripts~/build_uniffi_locally.sh macos [build_type]
# Build types: debug (default), release
```
- Plain-cargo equivalent of `cargo make build` in the crate's `Makefile.toml`, with a selectable build type
- Installs `liblivekit_uniffi.dylib` next to `liblivekit_ffi.dylib` in `Runtime/Plugins/ffi-macos-arm64/`, then regenerates the UniFFI C# bindings from it with `Scripts~/generate_uniffi_bindings.sh`
- After the build, Unity must be restarted to load the new dylib

### Generate the UniFFI C# bindings
```bash
# Defaults to the installed Runtime/Plugins/ffi-macos-arm64/liblivekit_uniffi.dylib
Scripts~/generate_uniffi_bindings.sh [library]
```
- Runs `uniffi-bindgen-cs` (release tag pinned in the script; must match the uniffi version of the Rust crate) from inside `client-sdk-rust~` because bindgen needs `cargo metadata`; configured by `Scripts~/uniffi/uniffi.toml` (namespace `LiveKit.Uniffi`, public API types)
- Post-processes the output with `Scripts~/uniffi/downgrade_uniffi_bindings.py` for C# 9, then replaces the `.cs` files in `Runtime/Scripts/UniFFI/` and removes `.meta` files of bindings that no longer exist. The folder always reflects the last library it was generated from; a library without UniFFI metadata leaves it untouched (with a warning)

### Other scripts in `Scripts~/`
- `download_libs.py` — downloads the prebuilt FFI binaries for all platforms; the release tag is pinned in `version.ini`
- `generate_proto.sh` — regenerates `Runtime/Scripts/Proto/` from the protobuf definitions in `client-sdk-rust~/livekit-ffi/protocol` (requires `protoc`)
- `uniffi/uniffi.toml` — uniffi-bindgen-cs config passed via `--config` (C# namespace, access modifier); not auto-discovered, so keep it in sync with `generate_uniffi_bindings.sh`
- `uniffi/downgrade_uniffi_bindings.py` — rewrites uniffi-bindgen-cs output for C# 9 in place (block-scoped namespace, inlined method-group locals, `Array.Empty<T>()` for `return []`, `IsExternalInit.cs` polyfill when records/`init` are present unless `--no-polyfill`); idempotent, called by `generate_uniffi_bindings.sh` with `--no-polyfill` because the Runtime assembly already ships `Internal/IsExternalInit.cs`
- `build_docs.sh`, `prepare_release.py`, `unity_test_results_utils.py` — docs generation, release preparation, CI test-result parsing

### Run tests
Tests require a local LiveKit server running:
```bash
# Install and run LiveKit server
curl -sSL https://get.livekit.io | bash
livekit-server --dev &
```
Tests run via Unity Test Framework (game-ci/unity-test-runner in CI; CI starts the server via livekit/dev-server-action). Tested against Unity 6000.0.49f1 and 2023.2.20f1. Tests are in `Tests/EditMode/` and `Tests/PlayMode/`.

## Architecture

### FFI Bridge Pattern
The SDK wraps a Rust native library (`liblivekit_ffi`) via P/Invoke. The communication flow:

1. **C# public API** (`Runtime/Scripts/`, feature folders) — `Room`, `Participant`, `Track`, audio/video sources, data streams, RPC
2. **FFI layer** (`Runtime/Scripts/Internal/`) — serializes requests via Protocol Buffers, sends through P/Invoke to Rust
3. **Native library** (`Runtime/Plugins/ffi-*/liblivekit_ffi.*`) — Rust implementation per platform/arch

`Runtime/Scripts/` is organized into feature folders:
- `Core/` — `Room`, `Participant`, `Track`, `TrackPublication`, `Rpc`, `E2EE`
- `Audio/` — audio sources (`RtcAudioSource`, `MicrophoneSource`, …), `AudioStream`, `AudioResampler`
- `Video/` — video sources (`CameraVideoSource`, `ScreenVideoSource`, …), `VideoStream`, YUV conversion
- `DataStreams/` — byte/text data streams and data tracks
- `TokenSource/` — token generation/fetching helpers and MonoBehaviour component
- `UniTask/` — optional UniTask integration (own asmdef: `livekit.unity.Runtime.UniTask.asmdef`)
- `Internal/`, `Proto/` — FFI plumbing and generated protobuf code
- `UniFFI/` — generated C# bindings for the UniFFI surface of the native library, currently `liblivekit_uniffi` from the experimental `livekit-uniffi` crate (namespace `LiveKit.Uniffi`; uniffi-bindgen-cs output, post-processed for C# 9)

Key internal files:
- `Internal/FFI/FFIClient.cs` — singleton managing request/response lifecycle with Rust via protobuf
- `Internal/FFI/Requests/FFIBridge.cs` — request factory
- `Internal/FFI/NativeMethods.cs` — P/Invoke declarations (`DllImport`)
- `Internal/Threading/YieldInstruction.cs` — custom awaitables for async FFI operations (coroutine-based)

### Proto/Generated Code
`Runtime/Scripts/Proto/` contains auto-generated C# from protobuf definitions in the Rust SDK. Do not edit these files manually; regenerate with `Scripts~/generate_proto.sh`.

`Runtime/Scripts/UniFFI/` contains auto-generated C# from `uniffi-bindgen-cs`. Do not edit these files manually; regenerate with `Scripts~/generate_uniffi_bindings.sh` (also run by `Scripts~/build_uniffi_locally.sh macos`), which includes the C# 9 post-processing.

### Native Plugins
10 platform/arch combinations in `Runtime/Plugins/ffi-{platform}-{arch}/`. These are large binary files tracked with Git LFS. The `.meta` files configure Unity platform targeting. `ffi-macos-arm64/` additionally holds `liblivekit_uniffi.dylib`, the locally built experimental UniFFI library.

### Samples
- `Samples~/Common` — shared components used by the other samples
- `Samples~/Basic` — minimal connection example, also used as the CI build target
- `Samples~/Meet` — more complete multi-participant example (LiveKit Meet-like), default project for `run_unity.sh`
- `Samples~/Agents` — connect to an agent and display the transcript

### Rust Submodule
`client-sdk-rust~/` is a git submodule pointing to the shared Rust SDK. The `~` suffix tells Unity to ignore the directory.

## Key Conventions

- Minimum Unity version: 2022.3
- Unsafe code is enabled via `Runtime/csc.rsp` and `Tests/csc.rsp`
- Assembly definitions: `livekit.unity.Runtime.asmdef`, `livekit.unity.Editor.asmdef`, `livekit.unity.Runtime.UniTask.asmdef`, plus test asmdefs
- Dependencies: `Google.Protobuf.dll` and `System.Runtime.CompilerServices.Unsafe.dll` shipped as managed plugins
