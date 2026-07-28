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

## License

PicCompressor is licensed under the [Apache License 2.0](LICENSE).
