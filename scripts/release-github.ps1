[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern("^\d+\.\d+\.\d+([\-+][0-9A-Za-z.-]+)?$")]
    [string]$Version,
    [string]$RepoUrl = "https://github.com/Diddlik/PicCompressor",
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$Channel = "win",
    # Without -Publish the release is left as a draft for a human to review.
    [switch]$Publish
)

# Uploads the VeloPack artifacts produced by publish-win-x64.ps1 to a GitHub
# release. This is the only step that talks to GitHub; it is kept separate from
# the build so the outward-facing action is explicit and reviewable. Releases are
# currently unsigned (Authenticode signing stays open under O-006).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$releasesOutput = Join-Path $root "artifacts\releases\win-x64"

if (-not (Test-Path -LiteralPath (Join-Path $releasesOutput "release-manifest.json"))) {
    throw "No release artifacts found in $releasesOutput. Run scripts/publish-win-x64.ps1 first."
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "A GitHub token is required (pass -Token or set GITHUB_TOKEN)."
}

# A prerelease suffix (e.g. 0.2.0-alpha.1) must not be offered as a stable update.
$isPrerelease = $Version -match "-"

$arguments = @(
    "upload", "github",
    "--outputDir", $releasesOutput,
    "--repoUrl", $RepoUrl,
    "--token", $Token,
    "--channel", $Channel,
    "--tag", "v$Version",
    "--releaseName", "PicCompressor $Version",
    "--merge", "true"
)
if ($isPrerelease) {
    $arguments += @("--pre", "true")
}
if ($Publish) {
    $arguments += @("--publish", "true")
}

& dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet tool restore failed with exit code $LASTEXITCODE."
}

& dotnet tool run vpk -- @arguments
if ($LASTEXITCODE -ne 0) {
    throw "vpk upload github failed with exit code $LASTEXITCODE."
}

Write-Host "Uploaded PicCompressor $Version to $RepoUrl ($(if ($Publish) { 'published' } else { 'draft' }))."
