#!/usr/bin/env bash
# Builds the native Jpegli wrapper for a Linux target architecture and runs its
# ABI and EXIF tests whenever the produced binaries can execute on this machine.
# Mirrors scripts/build-native-win-x64.ps1. Requires cmake >= 3.25, a C/C++
# toolchain (gcc or clang) and git; cross builds additionally require the
# matching gcc cross toolchain. The compiler is never invoked through a shell
# interpreter.
set -euo pipefail

usage() {
    echo "Usage: build-native-linux.sh [Release|Debug] [--arch x64|arm64]" >&2
    exit 2
}

normalize_architecture() {
    case "$1" in
        x64|amd64|x86_64) echo "x64" ;;
        arm64|aarch64) echo "arm64" ;;
        *) echo "Unsupported architecture: $1" >&2; exit 2 ;;
    esac
}

CONFIGURATION="Release"
ARCHITECTURE="$(normalize_architecture "$(uname -m)")"

while [ $# -gt 0 ]; do
    case "$1" in
        --arch)
            [ $# -ge 2 ] || usage
            ARCHITECTURE="$(normalize_architecture "$2")"
            shift 2
            ;;
        Release|Debug)
            CONFIGURATION="$1"
            shift
            ;;
        *) usage ;;
    esac
done

HOST_ARCHITECTURE="$(normalize_architecture "$(uname -m)")"
RUNTIME_IDENTIFIER="linux-$ARCHITECTURE"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_DIRECTORY="${OUTPUT_DIRECTORY:-$ROOT/artifacts/native/$RUNTIME_IDENTIFIER}"
BUILD_DIRECTORY="${BUILD_DIRECTORY:-$ROOT/native/build-$RUNTIME_IDENTIFIER}"
NATIVE_LIBRARY="$OUTPUT_DIRECTORY/piccompressor_native.so"

TOOLCHAIN_ARGUMENTS=()
if [ "$ARCHITECTURE" != "$HOST_ARCHITECTURE" ]; then
    if [ "$ARCHITECTURE" != "arm64" ]; then
        echo "Cross building $RUNTIME_IDENTIFIER on $HOST_ARCHITECTURE is not supported." >&2
        exit 2
    fi

    command -v aarch64-linux-gnu-gcc >/dev/null
    command -v aarch64-linux-gnu-g++ >/dev/null
    TOOLCHAIN_ARGUMENTS=(
        -DCMAKE_SYSTEM_NAME=Linux
        -DCMAKE_SYSTEM_PROCESSOR=aarch64
        -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc
        -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++
    )
fi

mkdir -p "$OUTPUT_DIRECTORY"

cmake -S "$ROOT/native" -B "$BUILD_DIRECTORY" \
  -G "Unix Makefiles" \
  -DCMAKE_BUILD_TYPE="$CONFIGURATION" \
  -DPC_ENABLE_JPEGLI=ON \
  -DPC_OUTPUT_DIR="$OUTPUT_DIRECTORY" \
  -DBUILD_TESTING=ON \
  "${TOOLCHAIN_ARGUMENTS[@]}"

cmake --build "$BUILD_DIRECTORY" \
  --target piccompressor_native piccompressor_native_tests piccompressor_exif_tests \
  --parallel "$(nproc)"

# A cross build produces binaries this machine cannot execute; they are verified
# on the target platform instead (requirement 19, MP-005).
if [ "$ARCHITECTURE" = "$HOST_ARCHITECTURE" ]; then
    LD_LIBRARY_PATH="$OUTPUT_DIRECTORY:${LD_LIBRARY_PATH:-}" "$BUILD_DIRECTORY/piccompressor_native_tests"
    "$BUILD_DIRECTORY/piccompressor_exif_tests"
else
    echo "Cross build for $RUNTIME_IDENTIFIER: native tests were not executed." >&2
fi

if [ ! -f "$NATIVE_LIBRARY" ]; then
    echo "Native build did not produce $NATIVE_LIBRARY" >&2
    exit 1
fi

echo "$NATIVE_LIBRARY"
