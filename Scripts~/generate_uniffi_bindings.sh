#!/bin/bash

# Regenerates the UniFFI C# bindings in Runtime/Scripts/UniFFI from a native library that
# carries UniFFI metadata and post-processes them for C# 9 (Unity).
#
# Usage: generate_uniffi_bindings.sh [LIBRARY]
#   LIBRARY   dylib to read the UniFFI metadata from. Defaults to the macOS
#             liblivekit_uniffi.dylib installed in Runtime/Plugins.
#
# Called by build_uniffi_locally.sh and build_ffi_locally.sh after a macOS build. Run it on
# its own to regenerate the bindings from the installed library without rebuilding.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RUST_ROOT="$ROOT/client-sdk-rust~"
OUT_DIR="$ROOT/Runtime/Scripts/UniFFI"
UNIFFI_CONFIG="$SCRIPT_DIR/uniffi/uniffi.toml"
DOWNGRADE="$SCRIPT_DIR/uniffi/downgrade_uniffi_bindings.py"
DEFAULT_LIBRARY="$ROOT/Runtime/Plugins/ffi-macos-arm64/liblivekit_uniffi.dylib"
# uniffi-bindgen-cs release. The "+vX.Y.Z" suffix is the uniffi version it targets and must
# match the uniffi version used by the crate the library was built from (client-sdk-rust~).
UNIFFI_BINDGEN_CS_REPO="https://github.com/NordSecurity/uniffi-bindgen-cs"
UNIFFI_BINDGEN_CS_TAG="v0.11.0+v0.31.0"

RED='\033[0;31m'
YELLOW='\033[0;33m'
GREEN='\033[0;32m'
RESET='\033[0m'

usage() {
    echo "Usage: $0 [LIBRARY]"
    echo ""
    echo "  LIBRARY   Native library with UniFFI metadata to generate the C# bindings from."
    echo "            Defaults to $DEFAULT_LIBRARY"
    exit 1
}

if [ $# -gt 1 ]; then
    echo -e "${RED}Error: Expected at most one argument.${RESET}"
    usage
fi

LIBRARY="${1:-$DEFAULT_LIBRARY}"
# uniffi-bindgen-cs runs from inside the Rust workspace below, so the path must be absolute.
case "$LIBRARY" in
    /*) ;;
    *) LIBRARY="$PWD/$LIBRARY" ;;
esac

if [ ! -f "$LIBRARY" ]; then
    echo -e "${RED}Error: Library not found: $LIBRARY${RESET}"
    exit 1
fi

if ! command -v uniffi-bindgen-cs > /dev/null 2>&1; then
    echo -e "${RED}Error: uniffi-bindgen-cs not found. Install it with:${RESET}"
    echo "  cargo install uniffi-bindgen-cs --git $UNIFFI_BINDGEN_CS_REPO --tag $UNIFFI_BINDGEN_CS_TAG"
    exit 1
fi

INSTALLED_TAG="v$(uniffi-bindgen-cs --version | awk '{print $2}')"
if [ "$INSTALLED_TAG" != "$UNIFFI_BINDGEN_CS_TAG" ]; then
    echo -e "${YELLOW}WARNING: uniffi-bindgen-cs $INSTALLED_TAG is installed, expected $UNIFFI_BINDGEN_CS_TAG (must match the uniffi version of the Rust crate).${RESET}"
fi

# Generate into a scratch directory first so that a library without UniFFI metadata (bindgen
# then exits 0 without writing anything) or a failing post-processing step cannot leave
# Runtime/Scripts/UniFFI half-updated or empty.
TMP_OUT="$(mktemp -d)"
trap 'rm -rf "$TMP_OUT"' EXIT

echo "Generating C# bindings from $LIBRARY..."
# uniffi-bindgen-cs resolves the crate's own uniffi.toml via `cargo metadata`, so it has to
# run from inside the Rust workspace.
pushd "$RUST_ROOT" > /dev/null
uniffi-bindgen-cs --library "$LIBRARY" --config "$UNIFFI_CONFIG" --out-dir "$TMP_OUT"
BINDGEN_STATUS=$?
popd > /dev/null

if [ $BINDGEN_STATUS -ne 0 ]; then
    echo -e "${RED}uniffi-bindgen-cs failed.${RESET}"
    exit 1
fi

if [ -z "$(find "$TMP_OUT" -maxdepth 1 -name '*.cs')" ]; then
    echo -e "${YELLOW}WARNING: No bindings were written. $(basename "$LIBRARY") contains no UniFFI metadata; existing bindings in $OUT_DIR left untouched.${RESET}"
    exit 0
fi

# --no-polyfill: the Runtime assembly already defines the IsExternalInit marker type that
# records / init accessors need (Runtime/Scripts/Internal/IsExternalInit.cs).
python3 "$DOWNGRADE" --no-polyfill "$TMP_OUT" || exit 1

# Replace the generated files: drop every previously generated .cs, move the new set in and
# remove .meta files whose .cs is gone. Regenerated files keep their .meta (and GUID); Unity
# creates .meta files for new ones on import.
mkdir -p "$OUT_DIR"
rm -f "$OUT_DIR"/*.cs
mv "$TMP_OUT"/*.cs "$OUT_DIR"/
for meta in "$OUT_DIR"/*.cs.meta; do
    [ -e "$meta" ] || continue
    [ -e "${meta%.meta}" ] || rm -f "$meta"
done

echo -e "${GREEN}Generated C# bindings in $OUT_DIR:${RESET}"
ls -1 "$OUT_DIR"/*.cs
