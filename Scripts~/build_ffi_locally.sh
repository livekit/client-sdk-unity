#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
MANIFEST="$ROOT/client-sdk-rust~/Cargo.toml"
BASE_DST="$ROOT/Runtime/Plugins"
BASE_TARGET="$ROOT/client-sdk-rust~/target"
UNIFFI_OUT_DIR="$ROOT/Runtime/Scripts/UniFFI"
UNIFFI_CONFIG="$SCRIPT_DIR/uniffi.toml"
# uniffi-bindgen-cs release. The "+vX.Y.Z" suffix is the uniffi version it targets
# and must match the uniffi version used by livekit-ffi in client-sdk-rust~.
UNIFFI_BINDGEN_CS_REPO="https://github.com/NordSecurity/uniffi-bindgen-cs"
UNIFFI_BINDGEN_CS_TAG="v0.11.0+v0.31.0"

RED='\033[0;31m'
YELLOW='\033[0;33m'
GREEN='\033[0;32m'
RESET='\033[0m'

# Prefer rustup-managed toolchains over any other rust in PATH (e.g. a Homebrew
# rust in /opt/homebrew/bin). The wrong rustc ignores rust-toolchain.toml and
# ships only its host target, so cross builds fail with "can't find crate for std".
CARGO_BIN="${CARGO_HOME:-$HOME/.cargo}/bin"
if [ -x "$CARGO_BIN/rustup" ]; then
    export PATH="$CARGO_BIN:$PATH"
fi

usage() {
    echo "Usage: $0 <platform> [build_type]"
    echo ""
    echo "Platforms:"
    echo "  macos       Build for aarch64-apple-darwin"
    echo "  android     Build for aarch64-linux-android"
    echo "  ios         Build for aarch64-apple-ios"
    echo ""
    echo "Build types (optional, defaults to 'debug'):"
    echo "  release     Optimized release build"
    echo "  debug       Debug build"
    echo ""
    echo "macOS builds also regenerate the UniFFI C# bindings in Runtime/Scripts/UniFFI"
    echo "(requires uniffi-bindgen-cs) and post-process them for C# 9."
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
        pushd "$ROOT/client-sdk-rust~" > /dev/null
        rustup target add aarch64-apple-darwin
        cargo build \
            --manifest-path "$MANIFEST" \
            $BUILD_FLAG \
            -p livekit-ffi \
            --target aarch64-apple-darwin
        BUILD_STATUS=$?
        popd > /dev/null

        SRC="$BASE_TARGET/aarch64-apple-darwin/$BUILD_DIR/liblivekit_ffi.dylib"
        DST="$BASE_DST/ffi-macos-arm64/liblivekit_ffi.dylib"
        ;;
    # ANDROID
    android)
        echo "Building for Android (aarch64-linux-android) [$BUILD_TYPE]..."
        pushd "$ROOT/client-sdk-rust~" > /dev/null
        rustup target add aarch64-linux-android
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
        rustup target add aarch64-apple-ios
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

# For iOS release, strip DWARF debug info from the static archive like CI does
if [ "$PLATFORM" = "ios" ] && [ "$BUILD_TYPE" = "release" ]; then
    echo ""
    echo "Stripping DWARF debug info from $(basename "$SRC")..."
    xcrun strip -S "$SRC"
    xcrun ranlib "$SRC"
fi

# Copy a built artifact into the package by writing a temp file next to the
# destination and renaming it into place, instead of overwriting in place.
# macOS caches code-signature state per inode: overwriting a signed dylib that a
# running Unity editor still has mapped makes every later load of that path die
# with SIGKILL "Code Signature Invalid" until the file gets a new inode.
install_file() {
    local src="$1" dst="$2" tmp="$2.tmp"
    if ! cp -f "$src" "$tmp" || ! mv -f "$tmp" "$dst"; then
        rm -f "$tmp"
        return 1
    fi
}

# Copy the built lib
echo ""
echo "Copying to $DST..."
install_file "$SRC" "$DST"

if [ $? -eq 0 ]; then
    echo -e "${GREEN}Copied $(basename "$DST") successfully.${RESET}"
else
    echo -e "${RED}Failed to copy $(basename "$DST"). Check that the source file exists and the destination directory is writable.${RESET}"
    exit 1
fi

# For android, also copy the built libwebrtc.jar
if [ "$PLATFORM" = "android" ]; then
    echo ""
    echo "Copying to $JAR_DST..."
    install_file "$JAR_SRC" "$JAR_DST"

    if [ $? -eq 0 ]; then
        echo -e "${GREEN}Copied $(basename "$JAR_DST") successfully.${RESET}"
    else
        echo -e "${RED}Failed to copy $(basename "$JAR_DST"). Check that the source file exists and the destination directory is writable.${RESET}"
        exit 1
    fi
fi

# For macOS, regenerate the UniFFI C# bindings from the freshly built dylib and
# post-process them for C# 9 (Unity). uniffi-bindgen-cs resolves the crate config
# via `cargo metadata`, so it has to run from inside the Rust workspace.
if [ "$PLATFORM" = "macos" ]; then
    echo ""
    if ! command -v uniffi-bindgen-cs > /dev/null 2>&1; then
        echo -e "${YELLOW}uniffi-bindgen-cs not found, skipping C# binding generation. Install it with:${RESET}"
        echo "  cargo install uniffi-bindgen-cs --git $UNIFFI_BINDGEN_CS_REPO --tag $UNIFFI_BINDGEN_CS_TAG"
    else
        INSTALLED_TAG="v$(uniffi-bindgen-cs --version | awk '{print $2}')"
        if [ "$INSTALLED_TAG" != "$UNIFFI_BINDGEN_CS_TAG" ]; then
            echo -e "${YELLOW}WARNING: uniffi-bindgen-cs $INSTALLED_TAG is installed, expected $UNIFFI_BINDGEN_CS_TAG (must match the uniffi version of livekit-ffi).${RESET}"
        fi

        echo "Generating C# bindings into $UNIFFI_OUT_DIR..."
        mkdir -p "$UNIFFI_OUT_DIR"
        pushd "$ROOT/client-sdk-rust~" > /dev/null
        uniffi-bindgen-cs --library "$SRC" --config "$UNIFFI_CONFIG" --out-dir "$UNIFFI_OUT_DIR"
        BINDGEN_STATUS=$?
        popd > /dev/null

        if [ $BINDGEN_STATUS -ne 0 ]; then
            echo -e "${RED}uniffi-bindgen-cs failed.${RESET}"
            exit 1
        fi

        # uniffi-bindgen-cs exits 0 without writing anything if the lib carries no UniFFI metadata
        if [ -z "$(find "$UNIFFI_OUT_DIR" -maxdepth 1 -name '*.cs' -newer "$SRC")" ]; then
            echo -e "${YELLOW}WARNING: No bindings were written. $(basename "$SRC") contains no UniFFI metadata; is client-sdk-rust~ on a commit where livekit-ffi exports UniFFI?${RESET}"
        else
            python3 "$SCRIPT_DIR/downgrade_uniffi_bindings.py" "$UNIFFI_OUT_DIR" || exit 1
            echo -e "${GREEN}Generated C# bindings successfully.${RESET}"
        fi
    fi

    echo ""
    echo -e "${YELLOW}WARNING: QUIT UNITY TO LOAD NEW LIB${RESET}"
fi
