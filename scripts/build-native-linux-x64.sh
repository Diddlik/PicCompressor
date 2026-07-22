#!/usr/bin/env bash
# Builds the native wrapper (Jpegli + Guetzli) for linux-x64 and runs its ABI and
# EXIF tests. Mirrors scripts/build-native-win-x64.ps1. Requires cmake >= 3.25, a
# C/C++ toolchain (gcc or clang) and git. The compiler is never invoked through a
# shell interpreter.
set -euo pipefail

CONFIGURATION="${1:-Release}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_DIRECTORY="${OUTPUT_DIRECTORY:-$ROOT/artifacts/native/linux-x64}"
BUILD_DIRECTORY="${BUILD_DIRECTORY:-$ROOT/native/build-linux-x64}"
NATIVE_LIBRARY="$OUTPUT_DIRECTORY/piccompressor_native.so"

mkdir -p "$OUTPUT_DIRECTORY"

cmake -S "$ROOT/native" -B "$BUILD_DIRECTORY" \
  -G "Unix Makefiles" \
  -DCMAKE_BUILD_TYPE="$CONFIGURATION" \
  -DPC_ENABLE_JPEGLI=ON \
  -DPC_ENABLE_GUETZLI=ON \
  -DPC_OUTPUT_DIR="$OUTPUT_DIRECTORY" \
  -DBUILD_TESTING=ON

cmake --build "$BUILD_DIRECTORY" \
  --target piccompressor_native piccompressor_native_tests piccompressor_exif_tests \
  --parallel "$(nproc)"

LD_LIBRARY_PATH="$OUTPUT_DIRECTORY:${LD_LIBRARY_PATH:-}" "$BUILD_DIRECTORY/piccompressor_native_tests"
"$BUILD_DIRECTORY/piccompressor_exif_tests"

if [ ! -f "$NATIVE_LIBRARY" ]; then
  echo "Native build did not produce $NATIVE_LIBRARY" >&2
  exit 1
fi

echo "$NATIVE_LIBRARY"
