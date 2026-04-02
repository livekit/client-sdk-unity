# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

LiveKit Unity SDK — a C# wrapper around LiveKit's Rust SDK using FFI (Foreign Function Interface) for real-time audio/video communication. Status: Developer Preview. Unity package name: `io.livekit.livekit-sdk`.

## Build & Development Commands

### Build FFI locally from Rust source
```bash
# Requires the client-sdk-rust~ submodule and Rust toolchain
BuildScripts~/build_ffi_locally.sh <platform> [build_type]
# Platforms: macos, android, ios
# Build types: debug (default), release
```
- **macOS**: requires `aarch64-apple-darwin` target
- **Android**: requires `cargo-ndk` and Android NDK
- **iOS**: builds static lib (`liblivekit_ffi.a`)
- After macOS builds, Unity must be restarted to load the new dylib

### Run tests
Tests require a local LiveKit server running:
```bash
# Install and run LiveKit server
curl -sSL https://get.livekit.io | bash
livekit-server --dev &
```
Tests run via Unity Test Framework (game-ci/unity-test-runner in CI). Tested against Unity 6000.0.49f1 and 2023.2.20f1. Tests are in `Tests/EditMode/` and `Tests/PlayMode/`.

## Architecture

### FFI Bridge Pattern
The SDK wraps a Rust native library (`liblivekit_ffi`) via P/Invoke. The communication flow:

1. **C# public API** (`Runtime/Scripts/*.cs`) — `Room`, `Participant`, `Track`, audio/video sources, data streams, RPC
2. **FFI layer** (`Runtime/Scripts/Internal/`) — serializes requests via Protocol Buffers, sends through P/Invoke to Rust
3. **Native library** (`Runtime/Plugins/ffi-*/liblivekit_ffi.*`) — Rust implementation per platform/arch

Key internal files:
- `FFIClient.cs` — singleton managing request/response lifecycle with Rust via protobuf
- `FFIBridge.cs` — request factory
- `NativeMethods.cs` — P/Invoke declarations (`DllImport`)
- `YieldInstruction.cs` — custom awaitables for async FFI operations (coroutine-based)

### Proto/Generated Code
`Runtime/Scripts/Proto/` contains auto-generated C# from protobuf definitions in the Rust SDK. Do not edit these files manually.

### Native Plugins
10 platform/arch combinations in `Runtime/Plugins/ffi-{platform}-{arch}/`. These are large binary files tracked with Git LFS. The `.meta` files configure Unity platform targeting.

### Samples
- `Samples~/Basic` — minimal connection example, also used as the CI build target
- `Samples~/Meet` — more complete multi-participant example

### Rust Submodule
`client-sdk-rust~/` is a git submodule pointing to the shared Rust SDK. The `~` suffix tells Unity to ignore the directory.

## Key Conventions

- Minimum Unity version: 2021.3
- Unsafe code is enabled via `csc.rsp`
- Assembly definitions: `livekit.unity.Runtime.asmdef`, `livekit.unity.Editor.asmdef`, plus test asmdefs
- Dependencies: `Google.Protobuf.dll` and `System.Runtime.CompilerServices.Unsafe.dll` shipped as managed plugins
