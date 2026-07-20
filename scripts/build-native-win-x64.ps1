[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "RelWithDebInfo")]
    [string]$Configuration = "Debug",
    [ValidateSet("Msvc", "Mingw")]
    [string]$Toolchain = "Msvc",
    [string]$ToolchainRoot = "C:\msys64\ucrt64",
    [string]$OutputDirectory,
    [string]$BuildDirectory
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$cmake = (Get-Command cmake -ErrorAction Stop).Source

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root "artifacts\native\win-x64\$Configuration"
}
if (-not $BuildDirectory) {
    $BuildDirectory = Join-Path $root "native\build-$($Toolchain.ToLowerInvariant())-win-x64"
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$BuildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$nativeLibrary = Join-Path $OutputDirectory "piccompressor_native.dll"

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

function Invoke-CheckedProcess(
    [string]$FilePath,
    [string[]]$Arguments
) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $startInfo.Environment.Clear()
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        if ($entry.Key -ieq "Path" -or
            $startInfo.Environment.ContainsKey([string]$entry.Key)) {
            continue
        }
        $startInfo.Environment[[string]$entry.Key] = [string]$entry.Value
    }
    $startInfo.Environment["PATH"] = $env:PATH

    $process = [Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Command failed with exit code $($process.ExitCode): $FilePath"
    }
}

$configureArguments = @(
    "-S", (Join-Path $root "native"),
    "-B", $BuildDirectory,
    "-DPC_ENABLE_JPEGLI=ON",
    "-DPC_OUTPUT_DIR=$($OutputDirectory.Replace('\', '/'))",
    "-DBUILD_TESTING=ON"
)

if ($Toolchain -eq "Mingw") {
    $gcc = Join-Path $ToolchainRoot "bin\gcc.exe"
    $gxx = Join-Path $ToolchainRoot "bin\g++.exe"
    $make = Join-Path $ToolchainRoot "bin\mingw32-make.exe"

    foreach ($tool in $gcc, $gxx, $make) {
        if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
            throw "Required UCRT64 tool not found: $tool"
        }
    }

    $env:PATH = "$(Join-Path $ToolchainRoot 'bin');$env:PATH"
    $configureArguments += @(
        "-G", "MinGW Makefiles",
        "-DCMAKE_BUILD_TYPE=$Configuration",
        "-DCMAKE_C_COMPILER=$($gcc.Replace('\', '/'))",
        "-DCMAKE_CXX_COMPILER=$($gxx.Replace('\', '/'))",
        "-DCMAKE_MAKE_PROGRAM=$($make.Replace('\', '/'))"
    )
}
else {
    $configureArguments += @("-A", "x64")
}

Invoke-CheckedProcess $cmake $configureArguments
Invoke-CheckedProcess $cmake @(
    "--build", $BuildDirectory,
    "--target", "piccompressor_native", "piccompressor_native_tests",
    "piccompressor_exif_tests",
    "--config", $Configuration,
    "--parallel", "4"
)

$testDirectory = if ($Toolchain -eq "Msvc") {
    Join-Path $BuildDirectory $Configuration
}
else {
    $BuildDirectory
}

$env:PATH = "$OutputDirectory;$env:PATH"
foreach ($test in "piccompressor_native_tests", "piccompressor_exif_tests") {
    Invoke-CheckedProcess (Join-Path $testDirectory "$test.exe") @()
}

if (-not (Test-Path -LiteralPath $nativeLibrary -PathType Leaf)) {
    throw "Native build did not produce $nativeLibrary"
}
if ($Toolchain -eq "Msvc" -and
    $Configuration -ne "Release" -and
    -not (Test-Path -LiteralPath (Join-Path $OutputDirectory "piccompressor_native.pdb"))) {
    throw "MSVC Debug build did not produce piccompressor_native.pdb."
}

Write-Output $nativeLibrary
