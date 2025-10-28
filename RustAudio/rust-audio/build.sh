#!/bin/bash
set -e  # Stop on error

LIB_NAME="librust_audio.dylib"
DEST_DIR="../Wrap/Libraries"
UNIVERSAL_OUT="$DEST_DIR/$LIB_NAME"

mkdir -p "$DEST_DIR"

echo "Building for x86_64 (Intel)..."
cargo build --release --target x86_64-apple-darwin

echo "Building for arm64 (Apple Silicon)..."
cargo build --release --target aarch64-apple-darwin

SRC_X86="target/x86_64-apple-darwin/release/$LIB_NAME"
SRC_ARM="target/aarch64-apple-darwin/release/$LIB_NAME"

if [ ! -f "$SRC_X86" ] || [ ! -f "$SRC_ARM" ]; then
  echo "One or both builds failed or missing: $SRC_X86 / $SRC_ARM"
  exit 1
fi

echo "Merging into universal binary..."
lipo -create -output "$UNIVERSAL_OUT" "$SRC_X86" "$SRC_ARM"

echo "Universal binary created at: $UNIVERSAL_OUT"

echo "Cleaning up build directories..."
rm -rf target

echo "Done."

