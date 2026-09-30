**English** · [Deutsch](README.de.md) · [Русский](README.ru.md)

# PicCompressor

Fast, local, privacy-friendly JPEG compression for desktop and command line.

## About

PicCompressor converts JPEG and PNG images to compact JPEG files using Jpegli.
All processing stays on your computer—images are never uploaded.

The desktop application and CLI share the same compression pipeline and safety rules.

## Highlights

- Fast Jpegli-based JPEG compression
- Desktop GUI and scriptable CLI
- Single files, batches, and recursive folders
- Drag and drop, clipboard import, cancellation, and retry
- Before/after preview, queue thumbnails, statistics, and update checks
- Configurable quality, chroma subsampling, and progressive encoding
- EXIF privacy and color-profile policies
- Local history and structured diagnostic logs
- Validated temporary output, collision protection, and explicit overwrite behavior
- Human-readable and versioned JSON CLI output

> [!CAUTION]
> **Replace original JPEG files** is an optional setting. The compressed file is
> fully validated before publication, but once published it permanently replaces
> the original. PNG inputs are rejected when this option is selected.

## Screenshots

<img width="1849" height="1226" alt="image" src="https://github.com/user-attachments/assets/4b5f3fda-08f2-4c7b-9e16-4ab48acdcd15" />
<img width="1849" height="1226" alt="image" src="https://github.com/user-attachments/assets/986824b4-e1fb-4fd5-bad5-53cbc2c4b089" />

## Download

[Open GitHub Releases](../../releases) and choose either the Windows x64
one-click installer or the portable ZIP. The package contains both the desktop
application and CLI.

> [!WARNING]
> PicCompressor is currently in alpha. Release packages are unsigned and may
> trigger a Windows SmartScreen warning.

## Current status

- Windows x64 GUI, CLI, and native Jpegli pipeline are verified.
- Jpegli is the sole encoder. The former Guetzli integration was removed because
  of its excessive runtime and resource usage.
- A self-contained Linux x64 CLI package can be built and passes its tests; the GUI, macOS, and
  `linux-arm64` packages are planned but not yet verified.
- The application UI supports English and German. The Russian README is documentation only.
- Signing, installation/update rollback tests, and the remaining release-security gates are still open.

## Build from source

Requirements for the current Windows development build:

- .NET SDK `10.0.302`
- Visual Studio with .NET desktop and C++ desktop workloads
- CMake `3.25` or newer
- PowerShell 7

```powershell
dotnet build PicCompressor.slnx
dotnet run --project src/PicCompressor.Desktop/PicCompressor.Desktop.csproj
```

The first build downloads and compiles the pinned Jpegli sources.

## CLI

```text
piccompressor <input> [<input> ...] [options]
```

Example:

```powershell
piccompressor "C:\Images" --recursive --quality 80 --output-dir "C:\Images\compressed"
```

Show all options:

```powershell
piccompressor --help
```

For scheduled folder scans (for example a NAS task), `--state` skips unchanged inputs as a
successful no-op, `--lock` prevents overlapping runs, and `--stable-for` leaves files that are
still being uploaded for a later run:

```bash
piccompressor /volume1/photo --recursive --output-dir /volume1/photo-compressed \
  --state /var/piccompressor/scan-state.json --lock /var/piccompressor/scan.lock \
  --stable-for 120 --parallelism 2 --timeout 300 --no-history
```

## Docker

`--config <path>` runs the CLI continuously over any number of watched folders, each with its own
output directory, state file and — optionally — its own compression settings. `mode` selects the
trigger: `Interval` rescans every `intervalSeconds`, `Watch` additionally reacts to debounced file
system events and still rescans periodically (`inotify` delivers nothing on mounted SMB/NFS
shares). `docker stop` ends the container in an orderly way; the current job finishes and no
unvalidated output is left behind.

The image is published to the GitHub Container Registry and rebuilt on every push to `main` and
on every version tag (`.github/workflows/container.yml`):

```bash
docker pull ghcr.io/diddlik/piccompressor:latest
docker compose -f docker/docker-compose.yml up -d
```

Available tags: `latest` (most recent final release), `X.Y.Z` and `X.Y` per version tag, and
`main` for the current development state. Docker never updates on its own; pick up a new image
with `docker compose pull && docker compose up -d` — on a schedule, or through the Watchtower
service prepared in [docker/docker-compose.yml](docker/docker-compose.yml).

Build locally instead of pulling:

```bash
docker build -f docker/Dockerfile -t piccompressor .
```

The image starts the integrated web service and scan worker. Before starting it, set
`PICCOMPRESSOR_WEB_PASSWORD` to a random password of at least 16 characters. The Compose default
binds port 8080 only to `127.0.0.1`; configure a Synology reverse-proxy HTTPS address targeting
`http://127.0.0.1:8080`, then sign in as `admin`. The web UI displays status and results, validates
and atomically saves `/config/piccompressor.json`, and can request an immediate scan. If the file
is missing, it starts with a safe example configuration.

The image expects writable `/config`, watched folders below `/data`, and writable `/state` for scan
state, locks, and history. See [docker/piccompressor.example.json](docker/piccompressor.example.json)
for a complete file. Build and run the image with the UID/GID that owns the mounted folders; the
container never runs as root.

The web login uses HTTP Basic Authentication. Expose port 8080 only on a trusted LAN, or put it
behind the Synology reverse proxy with HTTPS and additional authentication; never publish it
unprotected to the internet. The container scans immediately at startup and then autonomously in
`Interval` or `Watch` mode, so a DSM task scheduler is not required.

Only `linux/amd64` is built and verified; `linux/arm64` is prepared through `TARGETARCH` but
untested. The desktop GUI and web UI are separate applications; the container runs the web UI and
scan worker together as one process.

## License

PicCompressor is licensed under the [Apache License 2.0](LICENSE).
