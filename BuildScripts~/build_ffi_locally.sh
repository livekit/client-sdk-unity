#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
MANIFEST="$ROOT/client-sdk-rust~/Cargo.toml"
BASE_DST="$ROOT/Runtime/Plugins"
BASE_TARGET="$ROOT/client-sdk-rust~/target"

RED='\033[0;31m'
YELLOW='\033[0;33m'
GREEN='\033[0;32m'
RESET='\033[0m'

usage() {
    echo "Usage: $0 <platform>"
    echo ""
    echo "Platforms:"
    echo "  macos       Build for aarch64-apple-darwin"
    echo "  android     Build for aarch64-linux-android"
    exit 1
}

if [ $# -ne 1 ]; then
    echo -e "${RED}Error: Expected exactly one argument.${RESET}"
    usage
fi

PLATFORM="$1"

case "$PLATFORM" in
    macos)
        echo "Building for macOS (aarch64-apple-darwin)..."
        cargo build \
            --manifest-path "$MANIFEST" \
            --release \
            --workspace \
            -p livekit \
            --target aarch64-apple-darwin
        BUILD_STATUS=$?

        SRC="$BASE_TARGET/aarch64-apple-darwin/release/liblivekit_ffi.dylib"
        DST="$BASE_DST/ffi-macos-arm64/liblivekit_ffi.dylib"
        ;;
    android)
        echo "Building for Android (aarch64-linux-android)..."
        pushd "$ROOT/client-sdk-rust~" > /dev/null
        cargo ndk \
            --target aarch64-linux-android \
            build \
            --release \
            -p livekit \
            --workspace \
            -v \
            --no-default-features \
            --features "rustls-tls-webpki-roots"
        BUILD_STATUS=$?
        popd > /dev/null

        SRC="$BASE_TARGET/aarch64-linux-android/release/liblivekit_ffi.so"
        DST="$BASE_DST/ffi-android-arm64/liblivekit_ffi.so"
        ;;
    *)
        echo -e "${RED}Error: Unknown platform '$PLATFORM'.${RESET}"
        usage
        ;;
esac

if [ $BUILD_STATUS -ne 0 ]; then
    echo -e "${RED}Build failed. Aborting copy.${RESET}"
    exit 1
fi

echo ""
echo "Copying to $DST..."
cp -f "$SRC" "$DST"

if [ $? -eq 0 ]; then
    echo -e "${GREEN}Copied $(basename "$DST") successfully.${RESET}"
    if [ "$PLATFORM" = "macos" ]; then
        echo ""
        echo -e "${YELLOW}WARNING: QUIT UNITY TO LOAD NEW LIB${RESET}"
    fi
else
    echo -e "${RED}Failed to copy $(basename "$DST"). Check that the source file exists and the destination directory is writable.${RESET}"
    exit 1
fi