# env-setup.ps1 - one-time toolchain install for building livekit-ffi on Windows (x86_64).
# Installs: VS2022 Build Tools (MSVC v143) + Windows 11 SDK, Git, Rust (MSVC), libclang, protoc.
# Safe to re-run; each installer skips what is already present.
# Prerequisite: Python on PATH (libclang installs via pip; build-win.ps1 locates libclang via python).

[CmdletBinding()]
param(
    [string] $ProtocDir = "$env:LOCALAPPDATA\protoc"   # where protoc is unpacked
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'            # no slow IWR progress bars
function Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }

# 0. Python is a hard prerequisite (libclang/protoc steps and build-win.ps1 shell out to it). Not auto-installed.
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw "python not found on PATH. Install Python 3 (winget install Python.Python.3.12) and re-run."
}

# 1. VS2022 Build Tools: C++ toolset (v143) + Windows 11 SDK.
#    Required: the prebuilt webrtc.lib uses VS2022 STL symbols and the Win11 SDK (NTDDI_WIN11_*).
Step "Visual Studio 2022 Build Tools (MSVC v143 + Windows 11 SDK)"
winget install --id Microsoft.VisualStudio.2022.BuildTools --source winget `
  --accept-package-agreements --accept-source-agreements `
  --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows11SDK.26100"

# 2. Git: build-win.ps1 clones the rust-sdks source (with nested submodules) to build from.
Step "Git"
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    winget install --id Git.Git --source winget --accept-package-agreements --accept-source-agreements
} else {
    Write-Host "    git already present"
}

# 3. Rust stable, MSVC host (provides cargo/rustc).
Step "Rust (stable, x86_64-pc-windows-msvc)"
if (Get-Command rustup -ErrorAction SilentlyContinue) {
    rustup default stable-x86_64-pc-windows-msvc
} else {
    $init = "$env:TEMP\rustup-init.exe"
    Invoke-WebRequest https://win.rustup.rs/x86_64 -OutFile $init
    & $init -y --default-host x86_64-pc-windows-msvc --default-toolchain stable --profile default
}

# 4. libclang: required by yuv-sys' bindgen. The PyPI package bundles libclang.dll (no admin).
Step "libclang (via pip)"
python -m pip install --upgrade libclang
$libclang = python -c "import clang, os; print(os.path.join(os.path.dirname(clang.__file__), 'native'))"
Write-Host "    LIBCLANG_PATH = $libclang"

# 5. protoc: required by the livekit-ffi build script (prost-build). Unpack the latest release.
Step "protoc (latest release)"
$rel   = Invoke-RestMethod -Headers @{ 'User-Agent' = 'build' } https://api.github.com/repos/protocolbuffers/protobuf/releases/latest
$asset = $rel.assets | Where-Object { $_.name -match 'protoc-.*-win64\.zip' } | Select-Object -First 1
$zip   = "$env:TEMP\$($asset.name)"
Invoke-WebRequest $asset.browser_download_url -OutFile $zip
New-Item -ItemType Directory -Force -Path $ProtocDir | Out-Null
tar -xf $zip -C $ProtocDir                              # -> $ProtocDir\bin\protoc.exe
Write-Host "    PROTOC = $ProtocDir\bin\protoc.exe ($($rel.tag_name))"

Step "Toolchain installed. Next: .\build-win.ps1"
