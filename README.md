**English** · [Deutsch](README.de.md) · [Русский](README.ru.md)

# PicCompressor

Local, privacy-friendly JPEG compression for desktop and command line.

## About

PicCompressor converts JPEG and PNG images to compact JPEG files with Jpegli. Processing stays on your computer; images are not uploaded.

The desktop application and CLI share the same compression pipeline and safety rules.

## Features

- Jpegli-based JPEG compression
- Desktop GUI and scriptable CLI
- Single files, batches, and recursive folders
- Drag and drop, cancellation, retry, and local history
- Configurable quality, chroma subsampling, and progressive encoding
- EXIF privacy and color-profile policies
- Validated temporary output before publishing the final file
- Collision protection and explicit overwrite behavior
- Human-readable and versioned JSON CLI output

<img width="1849" height="1226" alt="image" src="https://github.com/user-attachments/assets/4b5f3fda-08f2-4c7b-9e16-4ab48acdcd15" />
<img width="1849" height="1226" alt="image" src="https://github.com/user-attachments/assets/986824b4-e1fb-4fd5-bad5-53cbc2c4b089" />


## Current status

PicCompressor is in alpha development. Windows x64 is currently verified. macOS, Linux, and additional architectures are planned but not yet verified.

The application UI currently supports English and German. This Russian README translation does not imply Russian UI support.

Public installers are not yet offered because release signing and the remaining security gates are still open.

## Quick start

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

## Releasing

Releases are hosted on GitHub Releases. Pushing a version tag builds the win-x64
package and publishes it through the `release` workflow
(`.github/workflows/release.yml`):

```powershell
git tag v0.2.0-alpha.1
git push origin v0.2.0-alpha.1
```

Releases are currently unsigned, so installation triggers a SmartScreen warning
until Authenticode signing is in place.

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
