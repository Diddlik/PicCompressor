using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.Tests;

/// <summary>
/// Discovery-Doppel für ViewModel-Tests: filtert unterstützte Endungen und kann für
/// Abbruch-Tests bis zum Abbruch blockieren. Die echte Enumeration prüft
/// <see cref="PicCompressor.Infrastructure.PhysicalInputDiscovery"/> gesondert.
/// </summary>
internal sealed class FakeInputDiscovery : IInputDiscovery
{
    private static readonly HashSet<string> Supported =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public bool BlockUntilCancelled { get; init; }

    public IReadOnlyList<DiscoveredInput> Discover(
        IEnumerable<string> inputPaths, bool recursive, string? excludedDirectory) =>
        Collect(inputPaths, recursive);

    public async Task<IReadOnlyList<DiscoveredInput>> DiscoverAsync(
        IReadOnlyList<string> inputPaths,
        bool recursive,
        string? excludedDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (BlockUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        var result = Collect(inputPaths, recursive);
        progress?.Report(result.Count);
        return result;
    }

    private static IReadOnlyList<DiscoveredInput> Collect(IEnumerable<string> paths, bool recursive)
    {
        var results = new List<DiscoveredInput>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                Add(path, results, seen);
            }
            else if (Directory.Exists(path))
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var file in Directory.EnumerateFiles(path, "*", option))
                {
                    Add(file, results, seen);
                }
            }
        }

        return results;
    }

    private static void Add(string path, List<DiscoveredInput> results, HashSet<string> seen)
    {
        var full = Path.GetFullPath(path);
        if (Supported.Contains(Path.GetExtension(full)) && seen.Add(full))
        {
            long size;
            try
            {
                size = new FileInfo(full).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                size = 0;
            }

            results.Add(new(full, "", size));
        }
    }
}

internal sealed class FakeCompressionService(
    Func<CompressionRequest, IProgress<CompressionProgress>?, CancellationToken, Task<CompressionOutcome>> handler)
    : ICompressionService
{
    public List<CompressionRequest> Requests { get; } = [];

    public int? LastBatchParallelism { get; private set; }

    public Task<CompressionOutcome> CompressAsync(
        CompressionRequest request,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return handler(request, progress, cancellationToken);
    }

    public async Task<IReadOnlyList<CompressionOutcome>> CompressBatchAsync(
        IReadOnlyList<CompressionRequest> requests,
        int maxParallelism,
        IProgress<CompressionBatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        LastBatchParallelism = maxParallelism;
        var outcomes = new List<CompressionOutcome>(requests.Count);
        for (var index = 0; index < requests.Count; index++)
        {
            var current = index;
            var jobProgress = progress is null
                ? null
                : new InlineProgress<CompressionProgress>(
                    value => progress.Report(new(current, value)));
            outcomes.Add(
                await CompressAsync(requests[index], jobProgress, cancellationToken));
        }

        return outcomes;
    }

    public static FakeCompressionService Succeeding(long outputSizeBytes = 100) =>
        new((request, progress, _) =>
        {
            progress?.Report(new CompressionProgress(JobStatus.Encoding, 50));
            return Task.FromResult(
                new CompressionOutcome(
                    JobStatus.Succeeded,
                    request.InputPath,
                    request.InputPath + ".out.jpg",
                    1000,
                    outputSizeBytes,
                    true,
                    null,
                    null,
                    null).Validate());
        });

    public static FakeCompressionService Failing(
        CompressionErrorCategory category = CompressionErrorCategory.EngineFailed) =>
        new((request, _, _) => Task.FromResult(
            CompressionOutcome.Failed(request.InputPath, 1000, category, "boom")));

    public static FakeCompressionService Throwing() =>
        new((_, _, _) => throw new InvalidOperationException("native crash"));

    public static FakeCompressionService Canceling() =>
        new((_, _, cancellationToken) =>
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            source.Cancel();
            source.Token.ThrowIfCancellationRequested();
            return Task.FromResult<CompressionOutcome>(null!);
        });
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal sealed class FakeFileActionService : IFileActionService
{
    public string? OpenedPath { get; private set; }

    public string? RevealedPath { get; private set; }

    public string? CopiedPath { get; private set; }

    public Task OpenFileAsync(string path)
    {
        OpenedPath = path;
        return Task.CompletedTask;
    }

    public Task RevealInFolderAsync(string path)
    {
        RevealedPath = path;
        return Task.CompletedTask;
    }

    public Task CopyPathAsync(string path)
    {
        CopiedPath = path;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Zwischenablage-Doppel: liefert vorgegebene Pfade. Der echte Adapter (Bilddaten als verwaltete
/// temporäre Eingabe) liegt im Desktop Host, die Ablage selbst prüft
/// <see cref="PicCompressor.Infrastructure.TemporaryInputStore"/> gesondert.
/// </summary>
internal sealed class FakeClipboardImportService(params string[] paths) : IClipboardImportService
{
    public int Reads { get; private set; }

    public Task<IReadOnlyList<string>> ReadImportPathsAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult<IReadOnlyList<string>>(paths);
    }
}

internal sealed class FakeEngineCatalogService(params EngineAvailability[] engines)
    : IEngineCatalogService
{
    public Task<IReadOnlyList<EngineAvailability>> GetEnginesAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EngineAvailability>>(engines);

    public static FakeEngineCatalogService JpegliAvailable() =>
        new(new EngineAvailability(EngineIds.Jpegli, true, "1.0", null));
}

internal sealed class ThrowingEngineCatalogService : IEngineCatalogService
{
    public Task<IReadOnlyList<EngineAvailability>> GetEnginesAsync(
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("catalogue unreachable");
}

internal sealed class RecordingHistoryService : IHistoryService
{
    private long nextId;

    public List<HistoryRecord> Records { get; } = [];

    public bool IsAvailable => true;

    public Task<IReadOnlyList<HistoryRecord>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HistoryRecord>>([.. Records]);

    public Task<HistoryRecord> AppendAsync(HistoryRecord record, CancellationToken cancellationToken)
    {
        var stored = record with { Id = ++nextId };
        Records.Insert(0, stored);
        return Task.FromResult(stored);
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        Records.RemoveAll(record => record.Id == id);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        Records.Clear();
        return Task.CompletedTask;
    }
}

internal static class TempFiles
{
    /// <summary>Legt eine leere Datei mit unterstützter Endung an; Inhalt ist hier unerheblich.</summary>
    public static string CreateImage(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[16]);
        return path;
    }

    public static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "piccompressor-gui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
