# Stop on error
$ErrorActionPreference = "Stop"

Write-Host "Building Rust project in release mode..."
cargo build --release

$source = "target/release/rust_audio.dll"
$destination = "../Wrap/Libraries/rust_audio.dll"

if (-Not (Test-Path $source)) {
    throw "Build succeeded but DLL not found at $source"
}

Write-Host "Copying $source to $destination"
Copy-Item -Path $source -Destination $destination -Force

Write-Host "Removing target directory..."
Remove-Item -Recurse -Force "target"

Write-Host "Build and copy complete."

