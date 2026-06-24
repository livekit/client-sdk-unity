# Building `livekit_ffi.dll` + `livekit_ffi.pdb` on Windows (x86_64)

> **Build tooling, not a Unity asset.** These scripts live under `BuildScripts~/` (the trailing
> `~` keeps the folder out of Unity's asset pipeline). They **download** the rust source into a
> gitignored `.src/` subfolder and build the native FFI library there; the source is never committed.
> They are **not** part of the shipped package and are not run from `Runtime/Plugins`.

Build the **`livekit-ffi`** crate as a release cdylib **with PDB debug symbols** on Windows, target `x86_64-pc-windows-msvc`. Everything is driven by `build.config.psd1`:

| File | What it is |
|------|------------|
| `build.config.psd1` | Config: `Tag` (which release to download/build) and `InstallToPlugins` (where the output goes). Edit this, not the scripts. |
| `env-setup.ps1` | One-time toolchain install: VS2022 Build Tools (MSVC v143) + Windows 11 SDK, Git, Rust (MSVC), libclang, protoc. Requires Python already on PATH. |
| `build-win.ps1` | Clones the `Tag` source (with nested submodules) into `.src/`, patches `[profile.release]` for PDB output, runs `cargo build`, and places the DLL + PDB per `InstallToPlugins`. |

## Quick start

```powershell
cd BuildScripts~/windows
notepad build.config.psd1   # set Tag + InstallToPlugins
.\env-setup.ps1             # one-time
.\build-win.ps1
```

## Configuration (`build.config.psd1`)

```powershell
@{
    Tag                   = 'livekit-ffi/v0.12.48'   # release to build; tags at the releases page below
    SourceDir             = '.src'                   # where to clone the source
    InstallToPlugins      = $true                    # see below
    CleanSourceAfterBuild = $false                   # delete the clone after a successful build
}
```

- `Tag`: a `livekit-ffi/vX.Y.Z` tag from <https://github.com/livekit/rust-sdks/releases>.
- `SourceDir`: where the source is cloned. Relative paths resolve against this tool folder; absolute paths (e.g. `C:\src`) work too. The default `.src` is gitignored; if you point it at another in-repo folder, add that to `.gitignore` yourself. An absolute path outside the repo also sidesteps the long-paths note below.
- `InstallToPlugins`:
  - `$true` -> the built `livekit_ffi.dll` + `livekit_ffi.pdb` **replace** the ones in `Runtime/Plugins/ffi-windows-x86_64/` (ready to ship).
  - `$false` -> they are dropped in this tool folder (`BuildScripts~/windows/`, gitignored) for inspection; copy them over yourself.
- `CleanSourceAfterBuild`: `$true` removes the `<SourceDir>\rust-sdks-<tag>` checkout once the DLL + PDB are placed; `$false` (default) keeps it so re-runs skip the clone.

The source is cloned to `<SourceDir>\rust-sdks-<tag>\` and reused on re-runs. The two output files:

| File | Purpose |
|------|---------|
| `livekit_ffi.dll` | Runtime cdylib. CRT is statically linked (`+crt-static`), so no VC++ Redistributable is needed. |
| `livekit_ffi.pdb` | Debug symbols, paired to that exact DLL (matching RSDS GUID). Needed only to debug/symbolicate, not to run. |

## Notes on the build

- **Source is downloaded, not vendored.** `build-win.ps1` does `git clone --recurse-submodules` of `livekit/rust-sdks` at the tag into `.src/` (gitignored); this also pulls the nested `yuv-sys/libyuv` + `livekit-protocol/protocol` submodules. webrtc is downloaded separately by `livekit-ffi/build.rs`. Nothing is added to this repo. (The `client-sdk-rust~` submodule that lives here is for C# proto generation via `generate_proto.sh`, not for this build.)
- **`+crt-static` comes from upstream, not the script.** The downloaded source's `.cargo/config.toml` sets `target-feature=+crt-static` for `x86_64-pc-windows-msvc`; cargo picks it up because the build runs from the source root. The script does not set it.
- **The profile patch is deliberate.** `build-win.ps1` rewrites the downloaded `Cargo.toml`'s `[profile.release]`: `debug = 2` + `split-debuginfo = "packed"` for the PDB, plus `lto`/`opt-level = "z"`/`panic = "abort"`/`codegen-units = 1`. Since the checkout under `.src/` is a throwaway, this leaves the repo untouched.
- **Long paths.** With the default `SourceDir = '.src'` the clone lives under this repo folder, so webrtc's deeply nested files can hit the 260-char limit. The script clones with `core.longpaths=true`; if a build step still trips on path length, set `SourceDir` to a short absolute path (e.g. `C:\src`), enable Windows long paths (`LongPathsEnabled=1`), or move the repo nearer the drive root.

## Why VS2022 + Windows 11 SDK

The prebuilt `webrtc.lib` references VS2022 STL symbols (`__std_find_trivial_*`) and is built against the Windows 11 SDK (`NTDDI_WIN11_*`). VS2019 or a Windows 10 SDK will not compile or link.

## Shipping notes

- To **run**: ship `livekit_ffi.dll` only. It loads on stock Windows 10/11 **x86_64** in any 64-bit host process.
- To **debug/symbolicate**: keep `livekit_ffi.pdb` paired with that exact DLL, or put it in a symbol store.
- Architecture: **x86_64** only. For arm64 build `aarch64-pc-windows-msvc` separately; a 64-bit DLL cannot load into a 32-bit process.

## Troubleshooting (symptom → cause)

| Symptom | Cause / fix |
|---|---|
| `python not found on PATH` (env-setup) | Install Python 3 (`winget install Python.Python.3.12`) and re-run. |
| `git not found` (build-win) | Git missing; re-run `env-setup.ps1` (it installs Git). |
| `yuv-sys` build.rs panics `NotFound` reading `include/libyuv` | libyuv submodule not fetched; delete the `.src\rust-sdks-*` checkout and re-run so the clone pulls submodules. |
| `bindgen`: `Unable to find libclang` | `LIBCLANG_PATH` unset/wrong → re-run `env-setup.ps1`. |
| build.rs: `Could not find protoc` | `PROTOC` unset → re-run `env-setup.ps1`. |
| `fileapi.h: error C2061: ... 'FILE_INFO_BY_HANDLE_CLASS'` | SDK too old; `NTDDI_WIN11_*` undefined → install a Windows 11 SDK. |
| `LNK2019/LNK2001: __std_find_trivial_*`, `__std_find_last_trivial_*` | Linking with VS2019 STL; build with the VS2022 v143 toolset. |

## Manual build

If you prefer not to use the scripts, see `build-win.ps1` for the exact steps: install the toolchain, `git clone --recurse-submodules` the tag, set the `[profile.release]` block, then run `cargo build --release -p livekit-ffi` from the source root in a shell with `vcvarsall.bat x64 <SDKVER>` loaded and `LIBCLANG_PATH` / `PROTOC` set.
