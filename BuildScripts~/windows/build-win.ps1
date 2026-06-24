# build-win.ps1 - build livekit_ffi.dll + livekit_ffi.pdb (release, x86_64-pc-windows-msvc).
# Self-contained: reads build.config.psd1, clones the rust-sdks source into .src\ (gitignored,
# never committed), patches [profile.release] for PDB output, runs cargo build, and places the
# DLL + PDB per the config flag. Run env-setup.ps1 first to install the toolchain.

[CmdletBinding()]
param(
    [string] $ProtocDir = "$env:LOCALAPPDATA\protoc"    # where env-setup.ps1 put protoc
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'
function Step($m) { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Info($m) { Write-Host "    $m"   -ForegroundColor DarkGray }

# --- 0. Config -------------------------------------------------------------
$cfgPath = Join-Path $PSScriptRoot 'build.config.psd1'
if (-not (Test-Path $cfgPath)) { throw "config not found: $cfgPath" }
$cfg = Import-PowerShellDataFile $cfgPath
$Tag = $cfg.Tag
if (-not $Tag) { throw "Tag not set in build.config.psd1" }
$installToPlugins = [bool]$cfg.InstallToPlugins
$cleanSource      = [bool]$cfg.CleanSourceAfterBuild

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$srcCfg   = if ($cfg.SourceDir) { $cfg.SourceDir } else { '.src' }      # clone root from config
$srcRoot  = if ([System.IO.Path]::IsPathRooted($srcCfg)) { $srcCfg } else { Join-Path $PSScriptRoot $srcCfg }
$repo     = Join-Path $srcRoot ("rust-sdks-" + ($Tag -replace '[\\/]', '-'))

# --- 1. Ensure toolchain ---------------------------------------------------
Step "Ensure toolchain"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw "git not found - run env-setup.ps1 first." }

$cargoCmd = Get-Command cargo -ErrorAction SilentlyContinue
$cargo = if ($cargoCmd) { $cargoCmd.Source } else { "$env:USERPROFILE\.cargo\bin\cargo.exe" }
if (-not (Test-Path $cargo)) { throw "cargo not found - run env-setup.ps1 first." }

# Locate VS2022 + vcvarsall via vswhere.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath  = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { throw "VS2022 C++ tools not found - run env-setup.ps1 first." }
$vcvars  = Join-Path $vsPath 'VC\Auxiliary\Build\vcvarsall.bat'

# Newest installed Windows SDK (build against the Win11 SDK headers).
$sdkVer = (Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\Include" -Directory |
           Where-Object Name -match '^10\.' | Sort-Object Name | Select-Object -Last 1).Name

# libclang + protoc: exported as env vars consumed by the build scripts.
$env:LIBCLANG_PATH = python -c "import clang, os; print(os.path.join(os.path.dirname(clang.__file__), 'native'))"
$env:PROTOC        = Join-Path $ProtocDir 'bin\protoc.exe'
if (-not (Test-Path $env:LIBCLANG_PATH)) { throw "libclang not found - run env-setup.ps1 first." }
if (-not (Test-Path $env:PROTOC))        { throw "protoc not found - run env-setup.ps1 first." }
Info "tag:           $Tag"
Info "source:        $repo"
Info "install:       $(if ($installToPlugins) { 'Runtime\Plugins\ffi-windows-x86_64' } else { 'tool folder' })"
Info "cargo:         $cargo"
Info "vcvars:        $vcvars"
Info "WinSDK:        $sdkVer"
Info "LIBCLANG_PATH: $env:LIBCLANG_PATH"
Info "PROTOC:        $env:PROTOC"

# --- 2. Download source for $Tag (into .src\, with nested submodules) ----------
# git clone --recurse-submodules pulls yuv-sys/libyuv + livekit-protocol/protocol cleanly;
# webrtc is downloaded later by livekit-ffi/build.rs. Per-tag folder, reused on re-run.
# core.longpaths handles webrtc's deep paths nested under this repo folder.
Step "Download rust-sdks source ($Tag)"
New-Item -ItemType Directory -Force -Path $srcRoot | Out-Null

if (Test-Path (Join-Path $repo '.git')) {
    Info "reusing existing checkout: $repo"
} else {
    if (Test-Path $repo) { Remove-Item $repo -Recurse -Force }   # clear a partial/failed checkout
    git -c core.longpaths=true clone --depth 1 --branch $Tag --recurse-submodules --shallow-submodules `
        https://github.com/livekit/rust-sdks.git $repo
    if ($LASTEXITCODE -ne 0) { throw "git clone failed for tag $Tag" }
    Info "source at: $repo"
}
if (-not (Test-Path (Join-Path $repo 'yuv-sys\libyuv\include\libyuv'))) {
    git -C $repo submodule update --init --recursive
}
if (-not (Test-Path (Join-Path $repo 'yuv-sys\libyuv\include\libyuv'))) { throw "libyuv submodule not populated" }

# --- 3. Patch [profile.release] (traceable build with PDB) -----------------
# Deliberately overrides the whole release profile: debug=2 makes the MSVC linker emit
# livekit_ffi.pdb; strip=symbols strips the DLL while the PDB keeps the symbols.
Step "Patch [profile.release]"
$cargoToml = Join-Path $repo 'Cargo.toml'
$profile = @'
[profile.release]
debug = 2
split-debuginfo = "packed"
strip = "symbols"
lto = true
opt-level = "z"
codegen-units = 1
panic = "abort"
'@
$text = Get-Content $cargoToml -Raw
$rx   = '(?ms)^\[profile\.release\].*?(?=^\[|\z)'
if ($text -match $rx) {
    $text = [regex]::Replace($text, $rx, ($profile + "`n"))
} else {
    $text = $text.TrimEnd() + "`n`n" + $profile + "`n"
}
[System.IO.File]::WriteAllText($cargoToml, $text, (New-Object System.Text.UTF8Encoding($false)))  # no BOM
Info "patched $cargoToml"

# --- 4. Build --------------------------------------------------------------
Step "cargo build --release -p livekit-ffi"
# Import the VS2022 + SDK environment into this session, then build.
$envText = cmd /c "`"$vcvars`" x64 $sdkVer >nul 2>&1 && set"
foreach ($line in $envText) { if ($line -match '^(.*?)=(.*)$') { Set-Item -Path "Env:$($matches[1])" -Value $matches[2] } }
$env:PATH = "$env:USERPROFILE\.cargo\bin;$env:PATH"

Push-Location $repo
cargo build --release -p livekit-ffi
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { throw "cargo build failed (exit $code)" }

# --- 5. Place output -------------------------------------------------------
$rel = Join-Path $repo 'target\release'
$dll = Join-Path $rel 'livekit_ffi.dll'
$pdb = Join-Path $rel 'livekit_ffi.pdb'
if (-not (Test-Path $dll)) { throw "build produced no DLL at $dll" }
if (-not (Test-Path $pdb)) { throw "build produced no PDB at $pdb" }

if ($installToPlugins) {
    $dest = Join-Path $repoRoot 'Runtime\Plugins\ffi-windows-x86_64'
} else {
    $dest = $PSScriptRoot
}
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $dll, $pdb $dest -Force

# --- 6. Clean source (optional) --------------------------------------------
# Safe here: step 4's Push/Pop-Location already returned us out of $repo.
if ($cleanSource) {
    Step "Clean source"
    Remove-Item $repo -Recurse -Force
    Info "removed $repo"
}

Step "Done"
Write-Host "    DLL -> $(Join-Path $dest 'livekit_ffi.dll')"
Write-Host "    PDB -> $(Join-Path $dest 'livekit_ffi.pdb')"
if (-not $installToPlugins) {
    Write-Host "    (InstallToPlugins is `$false; copy these into Runtime\Plugins\ffi-windows-x86_64\ to ship.)"
}
