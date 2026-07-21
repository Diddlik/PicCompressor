using System.Text.Json;

namespace PicCompressor.Cli.Tests;

public sealed class CliApplicationTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-cli-{Guid.NewGuid():N}");

    public CliApplicationTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, "input.png"),
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
    }

    [Fact]
    public async Task RunAsync_dry_run_plans_folder_without_writing_output()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            [directory, "--dry-run", "--json"],
            output,
            error);

        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, exitCode);
        Assert.True(json.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("plans").EnumerateArray());
        Assert.Empty(Directory.GetFiles(directory, "*.jpg"));
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task RunAsync_records_every_result_in_the_history()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var store = new RecordingHistoryStore();

        var exitCode = await CliApplication.RunAsync(
            [Path.Combine(directory, "input.png"), "--output-dir", Path.Combine(directory, "out")],
            output,
            error,
            store);

        Assert.Equal(0, exitCode);
        var entry = Assert.Single(store.Entries);
        // Absolute paths count as sensitive data (requirement 13.1).
        Assert.Equal("input.png", entry.FileName);
        Assert.Equal("jpegli", entry.EngineId);
        Assert.Equal(PicCompressor.Domain.JobStatus.Succeeded, entry.Status);
    }

    [Fact]
    public async Task RunAsync_skips_the_history_when_disabled()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var store = new RecordingHistoryStore();

        await CliApplication.RunAsync(
            [
                Path.Combine(directory, "input.png"),
                "--output-dir", Path.Combine(directory, "out"),
                "--no-history"
            ],
            output,
            error,
            store);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task RunAsync_dry_run_writes_no_history()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var store = new RecordingHistoryStore();

        await CliApplication.RunAsync([directory, "--dry-run"], output, error, store);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task RunAsync_uses_the_guetzli_engine_when_selected()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var store = new RecordingHistoryStore();

        var exitCode = await CliApplication.RunAsync(
            [
                Path.Combine(directory, "input.png"),
                "--engine", "guetzli",
                "--output-dir", Path.Combine(directory, "out")
            ],
            output,
            error,
            store);

        Assert.Equal(0, exitCode);
        var entry = Assert.Single(store.Entries);
        Assert.Equal("guetzli", entry.EngineId);
        Assert.Equal(PicCompressor.Domain.JobStatus.Succeeded, entry.Status);
    }

    [Fact]
    public async Task RunAsync_rejects_guetzli_below_its_quality_floor()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            [Path.Combine(directory, "input.png"), "--engine", "guetzli", "--quality", "50"],
            output,
            error);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_rejects_an_unknown_engine()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            [Path.Combine(directory, "input.png"), "--engine", "webp"],
            output,
            error);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task RunAsync_reports_a_history_failure_without_failing_the_job()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            [
                Path.Combine(directory, "input.png"),
                "--output-dir", Path.Combine(directory, "out"),
                "--json"
            ],
            output,
            error,
            new FailingHistoryStore());

        using var json = JsonDocument.Parse(output.ToString());
        // A persistence failure must not turn a correct encode into an error
        // (requirement 14.4).
        Assert.Equal(0, exitCode);
        Assert.Equal(
            "Succeeded",
            json.RootElement.GetProperty("results")[0].GetProperty("Status").GetString());
        Assert.Contains(
            "not recorded",
            json.RootElement.GetProperty("historyWarning").GetString());
    }

    [Fact]
    public async Task RunAsync_writes_a_structured_log_without_full_paths()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logPath = Path.Combine(directory, "run.jsonl");

        await CliApplication.RunAsync(
            [
                Path.Combine(directory, "input.png"),
                "--output-dir", Path.Combine(directory, "out"),
                "--no-history",
                "--log", logPath
            ],
            output,
            error);

        var lines = File.ReadAllLines(logPath);
        var entry = Assert.Single(lines);
        using var document = JsonDocument.Parse(entry);
        Assert.Equal("Cli", document.RootElement.GetProperty("Component").GetString());
        Assert.Equal("input.png", document.RootElement.GetProperty("FileName").GetString());
        Assert.NotEqual(
            Guid.Empty,
            document.RootElement.GetProperty("JobId").GetGuid());
        // Logs enthalten keine unnötigen vollständigen Pfade (Abschnitt 13.3).
        Assert.DoesNotContain(directory, entry, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private sealed class RecordingHistoryStore : PicCompressor.Application.ICompressionHistoryStore
    {
        public List<PicCompressor.Application.CompressionHistoryEntry> Entries { get; } = [];

        public Task<IReadOnlyList<PicCompressor.Application.CompressionHistoryEntry>> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PicCompressor.Application.CompressionHistoryEntry>>(
                Entries);

        public Task<PicCompressor.Application.CompressionHistoryEntry> AppendAsync(
            PicCompressor.Application.CompressionHistoryEntry entry,
            CancellationToken cancellationToken)
        {
            var stored = entry with { Id = Entries.Count + 1 };
            Entries.Add(stored);
            return Task.FromResult(stored);
        }

        public Task DeleteAsync(long id, CancellationToken cancellationToken)
        {
            Entries.RemoveAll(entry => entry.Id == id);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Entries.Clear();
            return Task.CompletedTask;
        }

        public Task<int> ApplyRetentionAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken) =>
            Task.FromResult(Entries.RemoveAll(entry => entry.CompletedAt < cutoff));
    }

    private sealed class FailingHistoryStore : PicCompressor.Application.ICompressionHistoryStore
    {
        public Task<IReadOnlyList<PicCompressor.Application.CompressionHistoryEntry>> GetAsync(
            CancellationToken cancellationToken) =>
            throw new IOException("history is unavailable");

        public Task<PicCompressor.Application.CompressionHistoryEntry> AppendAsync(
            PicCompressor.Application.CompressionHistoryEntry entry,
            CancellationToken cancellationToken) =>
            throw new IOException("history is unavailable");

        public Task DeleteAsync(long id, CancellationToken cancellationToken) =>
            throw new IOException("history is unavailable");

        public Task ClearAsync(CancellationToken cancellationToken) =>
            throw new IOException("history is unavailable");

        public Task<int> ApplyRetentionAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken) =>
            throw new IOException("history is unavailable");
    }
}
