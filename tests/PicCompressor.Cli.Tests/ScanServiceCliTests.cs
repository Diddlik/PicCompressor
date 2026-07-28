using System.Diagnostics;
using PicCompressor.Infrastructure;

namespace PicCompressor.Cli.Tests;

/// <summary>
/// Der konfigurationsgesteuerte Dauerbetrieb (D-055, D-056): mehrere überwachte Ordner mit eigenen
/// Einstellungen und eigenem Zustand, ein Lauf-Lock für den ganzen Prozess und ein geordnetes Ende
/// über den Abbruch.
/// </summary>
public sealed class ScanServiceCliTests : IDisposable
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-daemon-{Guid.NewGuid():N}");

    public ScanServiceCliTests()
    {
        Directory.CreateDirectory(Path.Combine(directory, "in-a"));
        Directory.CreateDirectory(Path.Combine(directory, "in-b"));
        File.WriteAllBytes(
            Path.Combine(directory, "in-a", "a.png"), Convert.FromBase64String(OnePixelPng));
        File.WriteAllBytes(
            Path.Combine(directory, "in-b", "b.png"), Convert.FromBase64String(OnePixelPng));
    }

    private string Path_(params string[] parts) =>
        Path.Combine([directory, .. parts]);

    private string WriteConfiguration(string? body = null, int intervalSeconds = 900)
    {
        var path = Path_("piccompressor.json");
        File.WriteAllText(
            path,
            body ?? $$"""
            {
              "schemaVersion": 1,
              "intervalSeconds": {{intervalSeconds}},
              "parallelism": 1,
              "stateDirectory": {{Quote(Path_("state"))}},
              "logPath": {{Quote(Path_("state", "log.jsonl"))}},
              "defaults": { "quality": 80, "largerOutput": "Keep" },
              "folders": [
                {
                  "name": "a",
                  "input": {{Quote(Path_("in-a"))}},
                  "output": {{Quote(Path_("out-a"))}}
                },
                {
                  "name": "b",
                  "input": {{Quote(Path_("in-b"))}},
                  "output": {{Quote(Path_("out-b"))}},
                  "settings": { "suffix": "_klein" }
                }
              ]
            }
            """);
        return path;
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal)}\"";

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CliApplication.RunAsync(arguments, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>
    /// Startet den Dauerbetrieb mit einem eigenen Abbruch-Token; im Prozess übernimmt das
    /// <c>Ctrl+C</c> beziehungsweise <c>SIGTERM</c> (D-057).
    /// </summary>
    private static async Task<int> RunDaemonAsync(
        string configurationPath,
        CancellationToken cancellationToken)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        return await CliApplication.RunConfiguredAsync(
            CliOptions.Parse(["--config", configurationPath]),
            output,
            error,
            historyStore: null,
            diagnosticLog: null,
            cancellationToken);
    }

    [Fact]
    public async Task One_cycle_processes_every_configured_folder_with_its_own_settings()
    {
        var configuration = WriteConfiguration();

        var run = await RunAsync(["--config", configuration, "--once"]);

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(Path_("out-a", "a_compressed.jpg")), run.Error);
        // Der ordnereigene Suffix überschreibt den globalen Vorgabewert.
        Assert.True(File.Exists(Path_("out-b", "b_klein.jpg")), run.Error);
    }

    [Fact]
    public async Task A_second_cycle_without_changes_encodes_nothing()
    {
        var configuration = WriteConfiguration();
        await RunAsync(["--config", configuration, "--once"]);
        var written = File.GetLastWriteTimeUtc(Path_("out-a", "a_compressed.jpg"));

        var second = await RunAsync(["--config", configuration, "--once"]);

        Assert.Equal(0, second.ExitCode);
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path_("out-a", "a_compressed.jpg")));
        // Jeder Ordner führt seinen eigenen Zustand.
        Assert.True(File.Exists(Path_("state", "a.json")));
        Assert.True(File.Exists(Path_("state", "b.json")));
    }

    [Fact]
    public async Task A_folder_without_a_supported_input_is_not_an_error()
    {
        Directory.CreateDirectory(Path_("empty"));
        var configuration = WriteConfiguration(
            $$"""
            {
              "schemaVersion": 1,
              "stateDirectory": {{Quote(Path_("state"))}},
              "folders": [
                {
                  "name": "leer",
                  "input": {{Quote(Path_("empty"))}},
                  "output": {{Quote(Path_("out-empty"))}}
                }
              ]
            }
            """);

        var run = await RunAsync(["--config", configuration, "--once"]);

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public async Task The_daemon_keeps_running_until_it_is_cancelled()
    {
        var configuration = WriteConfiguration(intervalSeconds: 1);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var daemon = RunDaemonAsync(configuration, cancellation.Token);
        var outputPath = Path_("out-a", "a_compressed.jpg");
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(outputPath) && stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(outputPath));
        Assert.False(daemon.IsCompleted);

        await cancellation.CancelAsync();

        // Ein geordnetes Ende ist kein Fehler.
        Assert.Equal(0, await daemon);
    }

    [Fact]
    public async Task A_held_lock_makes_the_run_a_no_op()
    {
        var lockPath = Path_("run.lock");
        var configuration = WriteConfiguration(
            $$"""
            {
              "schemaVersion": 1,
              "stateDirectory": {{Quote(Path_("state"))}},
              "lockPath": {{Quote(lockPath)}},
              "folders": [
                {
                  "name": "a",
                  "input": {{Quote(Path_("in-a"))}},
                  "output": {{Quote(Path_("out-a"))}}
                }
              ]
            }
            """);
        using var held = RunLock.TryAcquire(lockPath);

        var run = await RunAsync(["--config", configuration, "--once"]);

        Assert.Equal(0, run.ExitCode);
        Assert.False(Directory.Exists(Path_("out-a")));
    }

    [Fact]
    public async Task An_invalid_configuration_is_a_usage_error()
    {
        var configuration = WriteConfiguration(
            """{ "schemaVersion": 1, "folders": [ { "name": "a", "input": "/in" } ] }""");

        var run = await RunAsync(["--config", configuration, "--once"]);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("output", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_configuration_file_is_a_usage_error()
    {
        var run = await RunAsync(["--config", Path_("absent.json"), "--once"]);

        Assert.Equal(2, run.ExitCode);
    }

    [Fact]
    public async Task Input_paths_and_settings_may_not_be_mixed_with_a_configuration()
    {
        var configuration = WriteConfiguration();

        var withPath = await RunAsync(["--config", configuration, directory]);
        var withOption = await RunAsync(["--config", configuration, "--quality", "50"]);

        Assert.Equal(2, withPath.ExitCode);
        Assert.Equal(2, withOption.ExitCode);
        Assert.Contains("--quality", withOption.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Once_without_a_configuration_is_a_usage_error()
    {
        var run = await RunAsync([directory, "--once"]);

        Assert.Equal(2, run.ExitCode);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
