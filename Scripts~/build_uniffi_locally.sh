#!/bin/bash

# Builds the experimental livekit-uniffi crate (client-sdk-rust~/livekit-uniffi), regenerates the
# UniFFI C# bindings from the resulting liblivekit_uniffi.dylib with generate_uniffi_bindings.sh
# and, only if that succeeded, installs the dylib next to liblivekit_ffi.dylib in Runtime/Plugins.
#
# The build is the plain-cargo equivalent of `cargo make build` in the crate's Makefile.toml,
# with a selectable build type instead of the release-only cargo-make task.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
MANIFEST="$ROOT/client-sdk-rust~/Cargo.toml"
BASE_DST="$ROOT/Runtime/Plugins"
BASE_TARGET="$ROOT/client-sdk-rust~/target"

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
    echo ""
    echo "Build types (optional, defaults to 'debug'):"
    echo "  release     Optimized release build"
    echo "  debug       Debug build"
    echo ""
    echo "The UniFFI C# bindings in Runtime/Scripts/UniFFI are regenerated from the new library"
    echo "with generate_uniffi_bindings.sh (requires uniffi-bindgen-cs) before it is installed;"
    echo "if that fails, neither the bindings nor the installed library change."
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
        echo "Building livekit-uniffi for macOS (aarch64-apple-darwin) [$BUILD_TYPE]..."
        pushd "$ROOT/client-sdk-rust~" > /dev/null
        rustup target add aarch64-apple-darwin
        cargo build \
            --manifest-path "$MANIFEST" \
            $BUILD_FLAG \
            -p livekit-uniffi \
            --target aarch64-apple-darwin
        BUILD_STATUS=$?
        popd > /dev/null

        SRC="$BASE_TARGET/aarch64-apple-darwin/$BUILD_DIR/liblivekit_uniffi.dylib"
        DST="$BASE_DST/ffi-macos-arm64/liblivekit_uniffi.dylib"
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

# Generate the C# bindings from the freshly built dylib BEFORE installing it. If they cannot
# be produced (e.g. the post-processing rejects the output), the previous library stays in
# place, so Runtime/Plugins never gets ahead of Runtime/Scripts/UniFFI: a mismatched pair
# throws in the bindings' contract/checksum check on first use.
echo ""
"$SCRIPT_DIR/generate_uniffi_bindings.sh" "$SRC" || exit 1

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

echo ""
echo -e "${YELLOW}WARNING: QUIT UNITY TO LOAD NEW LIB${RESET}"
