using System.Text.Json;
using PicCompressor.Infrastructure;

namespace PicCompressor.Cli.Tests;

/// <summary>
/// Der wiederkehrende Scan eines Ordners (MP-005): unveränderte Eingaben sind ein erfolgreicher
/// No-op, eine Änderung erzeugt genau einen neuen Job, überlappende Läufe und noch wachsende
/// Dateien werden nicht verarbeitet.
/// </summary>
public sealed class IncrementalScanCliTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-scan-cli-{Guid.NewGuid():N}");

    private readonly string inputPath;
    private readonly string outputDirectory;
    private readonly string statePath;

    public IncrementalScanCliTests()
    {
        Directory.CreateDirectory(directory);
        inputPath = Path.Combine(directory, "input.png");
        outputDirectory = Path.Combine(directory, "out");
        statePath = Path.Combine(directory, "state.json");
        File.WriteAllBytes(
            inputPath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
    }

    private async Task<(int ExitCode, JsonDocument Json)> ScanAsync(params string[] extraArguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CliApplication.RunAsync(
            [
                directory,
                "--output-dir", outputDirectory,
                "--larger-output", "keep",
                "--no-history",
                "--state", statePath,
                "--json",
                .. extraArguments
            ],
            output,
            error);
        return (exitCode, JsonDocument.Parse(output.ToString()));
    }

    private string OutputPath => Path.Combine(outputDirectory, "input_compressed.jpg");

    [Fact]
    public async Task Second_unchanged_scan_is_a_successful_no_op()
    {
        var first = await ScanAsync();
        Assert.Equal(0, first.ExitCode);
        Assert.Single(first.Json.RootElement.GetProperty("results").EnumerateArray());
        var written = File.GetLastWriteTimeUtc(OutputPath);

        var second = await ScanAsync();

        Assert.Equal(0, second.ExitCode);
        Assert.Empty(second.Json.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal(1, second.Json.RootElement.GetProperty("unchangedCount").GetInt32());
        // Ohne Kodierung bleibt die vorhandene Ausgabe unangetastet.
        Assert.Equal(written, File.GetLastWriteTimeUtc(OutputPath));
    }

    [Fact]
    public async Task Changed_input_is_processed_exactly_once_and_replaces_its_own_output()
    {
        await ScanAsync();
        File.SetLastWriteTimeUtc(inputPath, DateTime.UtcNow.AddMinutes(5));

        var second = await ScanAsync();

        // Ohne den aufgezeichneten eigenen Ausgabepfad wäre das mit `--collision skip` ein
        // OutputConflict und Exit-Code 8.
        Assert.Equal(0, second.ExitCode);
        var result = Assert.Single(second.Json.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal("Succeeded", result.GetProperty("Status").GetString());
        Assert.Equal(OutputPath, result.GetProperty("OutputPath").GetString());
        Assert.Single(Directory.GetFiles(outputDirectory, "*.jpg"));
    }

    [Fact]
    public async Task Changed_settings_produce_exactly_one_new_job()
    {
        await ScanAsync();

        var second = await ScanAsync("--quality", "60");

        Assert.Equal(0, second.ExitCode);
        Assert.Single(second.Json.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal(0, second.Json.RootElement.GetProperty("unchangedCount").GetInt32());
    }

    [Fact]
    public async Task An_input_that_is_still_changing_is_left_for_a_later_run()
    {
        var scan = await ScanAsync("--stable-for", "3600");

        Assert.Equal(0, scan.ExitCode);
        Assert.Empty(scan.Json.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal(1, scan.Json.RootElement.GetProperty("unstableCount").GetInt32());
        Assert.False(Directory.Exists(outputDirectory));
    }

    [Fact]
    public async Task A_second_overlapping_run_does_nothing()
    {
        var lockPath = Path.Combine(directory, "run.lock");
        using var held = RunLock.TryAcquire(lockPath);

        var scan = await ScanAsync("--lock", lockPath);

        Assert.Equal(0, scan.ExitCode);
        Assert.True(scan.Json.RootElement.GetProperty("skippedBecauseLocked").GetBoolean());
        Assert.False(Directory.Exists(outputDirectory));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
