[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern("^\d+\.\d+\.\d+([\-+][0-9A-Za-z.-]+)?$")]
    [string]$Version,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ToolchainRoot = "C:\msys64\ucrt64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $root "artifacts"
$nativeOutput = Join-Path $artifactsRoot "native\win-x64\Release-Mingw"
$publishOutput = Join-Path $artifactsRoot "publish\win-x64"
$releasesOutput = Join-Path $artifactsRoot "releases\win-x64"
$applicationIcon = Join-Path $root "assets\PicCompressor.ico"
$buildDirectory = Join-Path $root "native\build-win-x64"
$nativeLibrary = Join-Path $nativeOutput "piccompressor_native.dll"
$jpegliSource = Join-Path $buildDirectory "_deps\jpegli-src"

function Invoke-Checked([scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE."
    }
}

function Reset-ArtifactDirectory([string]$Path) {
    $resolvedArtifacts = [IO.Path]::GetFullPath($artifactsRoot)
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith(
        $resolvedArtifacts + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside artifacts: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedPath | Out-Null
}

Reset-ArtifactDirectory $nativeOutput
Reset-ArtifactDirectory $publishOutput
Reset-ArtifactDirectory $releasesOutput

& (Join-Path $PSScriptRoot "build-native-win-x64.ps1") `
    -Configuration $Configuration `
    -Toolchain Mingw `
    -ToolchainRoot $ToolchainRoot `
    -OutputDirectory $nativeOutput `
    -BuildDirectory $buildDirectory

Invoke-Checked {
    & dotnet publish (Join-Path $root "src\PicCompressor.Cli\PicCompressor.Cli.csproj") `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $publishOutput `
        "-p:PicCompressorNativeLibrary=$nativeLibrary"
}
Invoke-Checked {
    & dotnet publish (Join-Path $root "src\PicCompressor.Desktop\PicCompressor.Desktop.csproj") `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $publishOutput `
        "-p:PicCompressorNativeLibrary=$nativeLibrary"
}

if (-not (Test-Path -LiteralPath (Join-Path $publishOutput "PicCompressor.Desktop.exe"))) {
    throw "Desktop publish did not produce PicCompressor.Desktop.exe."
}

$licenseDirectory = Join-Path $publishOutput "licenses"
New-Item -ItemType Directory -Path $licenseDirectory | Out-Null
$licenses = @{
    "jpegli-LICENSE" = Join-Path $jpegliSource "LICENSE"
    "highway-LICENSE" = Join-Path $jpegliSource "third_party\highway\LICENSE"
    "skcms-LICENSE" = Join-Path $jpegliSource "third_party\skcms\LICENSE"
    "libpng-LICENSE" = Join-Path $jpegliSource "third_party\libpng\LICENSE"
    "zlib-LICENSE" = Join-Path $jpegliSource "third_party\zlib\LICENSE"
}
foreach ($entry in $licenses.GetEnumerator()) {
    Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $licenseDirectory $entry.Key)
}

$smokeDirectory = Join-Path ([IO.Path]::GetTempPath()) "piccompressor-package-smoke-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $smokeDirectory | Out-Null
try {
    $inputPath = Join-Path $smokeDirectory "input.png"
    [IO.File]::WriteAllBytes(
        $inputPath,
        [Convert]::FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="))
    $json = & dotnet (Join-Path $publishOutput "PicCompressor.Cli.dll") `
        $inputPath --output-dir $smokeDirectory --larger-output keep --json
    if ($LASTEXITCODE -ne 0) {
        throw "Published CLI smoke test failed with exit code $LASTEXITCODE."
    }
    $result = $json | ConvertFrom-Json
    if ($result.result.status -ne "Succeeded" -or -not $result.result.outputPublished) {
        throw "Published CLI smoke test did not publish a successful output."
    }
}
finally {
    $resolvedSmoke = [IO.Path]::GetFullPath($smokeDirectory)
    if ($resolvedSmoke.StartsWith(
        [IO.Path]::GetFullPath([IO.Path]::GetTempPath()),
        [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedSmoke -Recurse -Force
    }
}

$lockText = Get-Content -Raw (Join-Path $root "native\dependencies.lock.cmake")
$revision = [regex]::Match(
    $lockText,
    'PC_JPEGLI_REVISION "([0-9a-f]{40})"').Groups[1].Value
$jpegliVersion = [regex]::Match(
    $lockText,
    'PC_JPEGLI_VERSION "([^"]+)"').Groups[1].Value
if (-not $revision -or -not $jpegliVersion) {
    throw "Could not read pinned Jpegli metadata."
}

$files = Get-ChildItem -LiteralPath $publishOutput -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($publishOutput, $_.FullName).Replace("\", "/")
            sizeBytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
$manifest = [ordered]@{
    schemaVersion = 1
    runtimeIdentifier = "win-x64"
    configuration = $Configuration
    jpegli = [ordered]@{
        version = $jpegliVersion
        sourceRevision = $revision
    }
    files = @($files)
}
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $publishOutput "manifest.json") -Encoding utf8NoBOM

Invoke-Checked {
    & dotnet tool restore
}
Invoke-Checked {
    & dotnet tool run vpk -- pack `
        --packId PicCompressor `
        --packVersion $Version `
        --packDir $publishOutput `
        --mainExe PicCompressor.Desktop.exe `
        --runtime win-x64 `
        --packTitle PicCompressor `
        --icon $applicationIcon `
        --outputDir $releasesOutput
}

$releaseFiles = Get-ChildItem -LiteralPath $releasesOutput -File |
    Sort-Object Name |
    ForEach-Object {
        [ordered]@{
            path = $_.Name
            sizeBytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
$releaseManifest = [ordered]@{
    schemaVersion = 1
    packId = "PicCompressor"
    version = $Version
    runtimeIdentifier = "win-x64"
    signed = $false
    files = @($releaseFiles)
}
$releaseManifest | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $releasesOutput "release-manifest.json") -Encoding utf8NoBOM

Write-Host "Published: $releasesOutput"
