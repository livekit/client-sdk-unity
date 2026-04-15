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
    echo "Usage: $0 <platform> [build_type]"
    echo ""
    echo "Platforms:"
    echo "  macos       Build for aarch64-apple-darwin"
    echo "  linux       Build for x86_64-unknown-linux-gnu"
    echo "  android     Build for aarch64-linux-android"
    echo "  ios         Build for aarch64-apple-ios"
    echo ""
    echo "Build types (optional, defaults to 'debug'):"
    echo "  release     Optimized release build"
    echo "  debug       Debug build"
    exit 1
}

if [ $# -lt 1 ] || [ $# -gt 2 ]; then
    echo -e "${RED}Error: Expected one or two arguments.${RESET}"
    usage
fi

PLATFORM="$1"
BUILD_TYPE="${2:-debug}"

case "$BUILD_TYPE" in
    release)
        BUILD_FLAG="--release"
        BUILD_DIR="release"
        ;;
    debug)
        BUILD_FLAG=""
        BUILD_DIR="debug"
        ;;
    *)
        echo -e "${RED}Error: Unknown build type '$BUILD_TYPE'. Expected 'release' or 'debug'.${RESET}"
        usage
        ;;
esac

case "$PLATFORM" in
    # MACOS
    macos)
        echo "Building for macOS (aarch64-apple-darwin) [$BUILD_TYPE]..."
        cargo build \
            --manifest-path "$MANIFEST" \
            $BUILD_FLAG \
            -p livekit-ffi \
            --target aarch64-apple-darwin
        BUILD_STATUS=$?

        SRC="$BASE_TARGET/aarch64-apple-darwin/$BUILD_DIR/liblivekit_ffi.dylib"
        DST="$BASE_DST/ffi-macos-arm64/liblivekit_ffi.dylib"
        ;;
    # LINUX
    linux)
        echo "Building for Linux (x86_64-unknown-linux-gnu) [$BUILD_TYPE]..."
        cargo build \
            --manifest-path "$MANIFEST" \
            $BUILD_FLAG \
            -p livekit-ffi \
            --target x86_64-unknown-linux-gnu
        BUILD_STATUS=$?

        SRC="$BASE_TARGET/x86_64-unknown-linux-gnu/$BUILD_DIR/liblivekit_ffi.so"
        DST="$BASE_DST/ffi-linux-x86_64/liblivekit_ffi.so"
        ;;
    # ANDROID
    android)
        echo "Building for Android (aarch64-linux-android) [$BUILD_TYPE]..."
        pushd "$ROOT/client-sdk-rust~" > /dev/null
        cargo ndk \
            --target aarch64-linux-android \
            build \
            $BUILD_FLAG \
            -p livekit-ffi \
            -v \
            --no-default-features \
            --features "rustls-tls-webpki-roots"
        BUILD_STATUS=$?
        popd > /dev/null

        SRC="$BASE_TARGET/aarch64-linux-android/$BUILD_DIR/liblivekit_ffi.so"
        DST="$BASE_DST/ffi-android-arm64/liblivekit_ffi.so"
        JAR_SRC="$BASE_TARGET/aarch64-linux-android/$BUILD_DIR/libwebrtc.jar"
        JAR_DST="$BASE_DST/ffi-android-arm64/libwebrtc.jar"
        ;;
    # IOS
    ios)
        echo "Building for iOS (aarch64-apple-ios) [$BUILD_TYPE]..."
        pushd "$ROOT/client-sdk-rust~/livekit-ffi" > /dev/null
        cargo rustc \
            --crate-type staticlib \
            $BUILD_FLAG \
            --target aarch64-apple-ios \
            --no-default-features \
            --features "rustls-tls-webpki-roots"
        BUILD_STATUS=$?
        popd > /dev/null

        SRC="$BASE_TARGET/aarch64-apple-ios/$BUILD_DIR/liblivekit_ffi.a"
        DST="$BASE_DST/ffi-ios-arm64/liblivekit_ffi.a"
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

# Copy the built lib
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

# For android, also copy the built libwebrtc.jar
if [ "$PLATFORM" = "android" ]; then
    echo ""
    echo "Copying to $JAR_DST..."
    cp -f "$JAR_SRC" "$JAR_DST"

    if [ $? -eq 0 ]; then
        echo -e "${GREEN}Copied $(basename "$JAR_DST") successfully.${RESET}"
    else
        echo -e "${RED}Failed to copy $(basename "$JAR_DST"). Check that the source file exists and the destination directory is writable.${RESET}"
        exit 1
    fi
fi