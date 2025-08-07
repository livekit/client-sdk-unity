#!/bin/bash
set -e  # Stop on error

echo "Building Rust project in release mode..."
cargo build --release

source="target/release/librust_audio.dylib"
destination="../Wrap/Libraries/librust_audio.dylib"

if [ ! -f "$source" ]; then
  echo "Build succeeded but DLL not found at $source" >&2
  exit 1
fi

echo "Copying $source to $destination"
cp -f "$source" "$destination"

echo "Removing target directory..."
rm -rf target

echo "Build and copy complete."

