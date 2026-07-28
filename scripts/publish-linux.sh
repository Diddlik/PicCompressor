#!/usr/bin/env bash
# Publishes the self-contained PicCompressor CLI for a Linux runtime identifier
# and packs it as a tarball with checksum manifests. The GUI is not part of the
# Linux packages; the CLI is what the Synology task scheduler runs (MP-005).
# Mirrors scripts/publish-win-x64.ps1.
set -euo pipefail

usage() {
    echo "Usage: publish-linux.sh --version <version> [--arch x64|arm64] [--configuration Release|Debug]" >&2
    exit 2
}

VERSION=""
ARCHITECTURE=""
CONFIGURATION="Release"

while [ $# -gt 0 ]; do
    case "$1" in
        --version) [ $# -ge 2 ] || usage; VERSION="$2"; shift 2 ;;
        --arch) [ $# -ge 2 ] || usage; ARCHITECTURE="$2"; shift 2 ;;
        --configuration) [ $# -ge 2 ] || usage; CONFIGURATION="$2"; shift 2 ;;
        *) usage ;;
    esac
done

[ -n "$VERSION" ] || usage
if ! printf '%s' "$VERSION" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$'; then
    echo "Invalid version: $VERSION" >&2
    exit 2
fi
case "$CONFIGURATION" in Release|Debug) ;; *) usage ;; esac

host_architecture() {
    case "$(uname -m)" in
        x86_64|amd64) echo "x64" ;;
        aarch64|arm64) echo "arm64" ;;
        *) echo "unsupported" ;;
    esac
}

HOST_ARCHITECTURE="$(host_architecture)"
ARCHITECTURE="${ARCHITECTURE:-$HOST_ARCHITECTURE}"
case "$ARCHITECTURE" in x64|arm64) ;; *) usage ;; esac

RUNTIME_IDENTIFIER="linux-$ARCHITECTURE"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS_ROOT="$ROOT/artifacts"
NATIVE_OUTPUT="$ARTIFACTS_ROOT/native/$RUNTIME_IDENTIFIER"
PUBLISH_OUTPUT="$ARTIFACTS_ROOT/publish/$RUNTIME_IDENTIFIER"
RELEASES_OUTPUT="$ARTIFACTS_ROOT/releases/$RUNTIME_IDENTIFIER"
BUILD_DIRECTORY="$ROOT/native/build-$RUNTIME_IDENTIFIER"
NATIVE_LIBRARY="$NATIVE_OUTPUT/piccompressor_native.so"
JPEGLI_SOURCE="$BUILD_DIRECTORY/_deps/jpegli-src"

reset_artifact_directory() {
    case "$1" in
        "$ARTIFACTS_ROOT"/*) ;;
        *) echo "Refusing to reset a directory outside artifacts: $1" >&2; exit 1 ;;
    esac
    rm -rf "$1"
    mkdir -p "$1"
}

reset_artifact_directory "$PUBLISH_OUTPUT"
reset_artifact_directory "$RELEASES_OUTPUT"

OUTPUT_DIRECTORY="$NATIVE_OUTPUT" BUILD_DIRECTORY="$BUILD_DIRECTORY" \
    "$(dirname "${BASH_SOURCE[0]}")/build-native-linux.sh" "$CONFIGURATION" --arch "$ARCHITECTURE"

# --artifacts-path keeps the RID-specific intermediates out of the source tree, so a
# publish for another RID does not overwrite an existing bin/obj state.
# PicCompressorTargetRid must be a global property rather than only the ProjectReference's
# AdditionalProperties: otherwise MSBuild builds Domain and Application under two sets of global
# properties and both instances write the same deps.json at the same time.
dotnet publish "$ROOT/src/PicCompressor.Cli/PicCompressor.Cli.csproj" \
    --configuration "$CONFIGURATION" \
    --runtime "$RUNTIME_IDENTIFIER" \
    --artifacts-path "$ARTIFACTS_ROOT/build/$RUNTIME_IDENTIFIER" \
    --self-contained true \
    --output "$PUBLISH_OUTPUT" \
    "-p:Version=$VERSION" \
    "-p:PicCompressorTargetRid=$RUNTIME_IDENTIFIER" \
    "-p:PicCompressorNativeLibrary=$NATIVE_LIBRARY"

[ -x "$PUBLISH_OUTPUT/PicCompressor.Cli" ] || {
    echo "Publish did not produce an executable PicCompressor.Cli." >&2
    exit 1
}
[ -f "$PUBLISH_OUTPUT/piccompressor_native.so" ] || {
    echo "Publish did not include piccompressor_native.so." >&2
    exit 1
}

mkdir -p "$PUBLISH_OUTPUT/licenses"
cp "$ROOT/LICENSE" "$PUBLISH_OUTPUT/licenses/PicCompressor-LICENSE"
cp "$JPEGLI_SOURCE/LICENSE" "$PUBLISH_OUTPUT/licenses/jpegli-LICENSE"
cp "$JPEGLI_SOURCE/third_party/highway/LICENSE" "$PUBLISH_OUTPUT/licenses/highway-LICENSE"
cp "$JPEGLI_SOURCE/third_party/skcms/LICENSE" "$PUBLISH_OUTPUT/licenses/skcms-LICENSE"
cp "$JPEGLI_SOURCE/third_party/libpng/LICENSE" "$PUBLISH_OUTPUT/licenses/libpng-LICENSE"
cp "$JPEGLI_SOURCE/third_party/zlib/LICENSE" "$PUBLISH_OUTPUT/licenses/zlib-LICENSE"

# The published binaries only run on this machine when host and target match.
if [ "$ARCHITECTURE" = "$HOST_ARCHITECTURE" ]; then
    SMOKE_DIRECTORY="$(mktemp -d)"
    trap 'rm -rf "$SMOKE_DIRECTORY"' EXIT
    printf '%s' \
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=' \
        | base64 -d > "$SMOKE_DIRECTORY/input.png"
    SMOKE_JSON="$("$PUBLISH_OUTPUT/PicCompressor.Cli" "$SMOKE_DIRECTORY/input.png" \
        --output-dir "$SMOKE_DIRECTORY" --larger-output keep --no-history --json)"
    case "$SMOKE_JSON" in
        *'"Status":"Succeeded"'*) ;;
        *) echo "Published CLI smoke test did not succeed: $SMOKE_JSON" >&2; exit 1 ;;
    esac
else
    echo "Cross build for $RUNTIME_IDENTIFIER: the published CLI was not smoke tested." >&2
fi

JPEGLI_REVISION="$(sed -n 's/.*PC_JPEGLI_REVISION "\([0-9a-f]\{40\}\)".*/\1/p' \
    "$ROOT/native/dependencies.lock.cmake")"
JPEGLI_VERSION="$(sed -n 's/.*PC_JPEGLI_VERSION "\([^"]*\)".*/\1/p' \
    "$ROOT/native/dependencies.lock.cmake")"
if [ -z "$JPEGLI_REVISION" ] || [ -z "$JPEGLI_VERSION" ]; then
    echo "Could not read pinned Jpegli metadata." >&2
    exit 1
fi

# Emits the "files" array of a manifest for every file below $1, relative to it.
write_file_entries() {
    local directory="$1"
    local first=1
    local path
    while IFS= read -r path; do
        [ "$first" -eq 1 ] || printf ',\n'
        first=0
        printf '    { "path": "%s", "sizeBytes": %s, "sha256": "%s" }' \
            "${path#./}" \
            "$(stat -c %s "$directory/$path")" \
            "$(sha256sum "$directory/$path" | cut -d' ' -f1)"
    done < <(cd "$directory" && find . -type f | LC_ALL=C sort)
    printf '\n'
}

# The manifest is built before it is written so that it does not list itself.
MANIFEST="$({
    printf '{\n  "schemaVersion": 1,\n  "runtimeIdentifier": "%s",\n' "$RUNTIME_IDENTIFIER"
    printf '  "configuration": "%s",\n' "$CONFIGURATION"
    printf '  "jpegli": { "version": "%s", "sourceRevision": "%s" },\n' \
        "$JPEGLI_VERSION" "$JPEGLI_REVISION"
    printf '  "files": [\n'
    write_file_entries "$PUBLISH_OUTPUT"
    printf '  ]\n}'
})"
printf '%s\n' "$MANIFEST" > "$PUBLISH_OUTPUT/manifest.json"

PACKAGE_NAME="PicCompressor-$VERSION-$RUNTIME_IDENTIFIER.tar.gz"
tar --create --gzip --file "$RELEASES_OUTPUT/$PACKAGE_NAME" \
    --directory "$PUBLISH_OUTPUT" .

RELEASE_MANIFEST="$({
    printf '{\n  "schemaVersion": 1,\n  "packId": "PicCompressor",\n'
    printf '  "version": "%s",\n  "runtimeIdentifier": "%s",\n  "signed": false,\n' \
        "$VERSION" "$RUNTIME_IDENTIFIER"
    printf '  "files": [\n'
    write_file_entries "$RELEASES_OUTPUT"
    printf '  ]\n}'
})"
printf '%s\n' "$RELEASE_MANIFEST" > "$RELEASES_OUTPUT/release-manifest.json"

echo "Published: $RELEASES_OUTPUT/$PACKAGE_NAME"
