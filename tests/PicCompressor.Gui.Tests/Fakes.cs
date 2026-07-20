using PicCompressor.Domain;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.Tests;

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
    public List<HistoryRecord> Records { get; } = [];

    public bool IsAvailable => true;

    public Task<IReadOnlyList<HistoryRecord>> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<HistoryRecord>>([.. Records]);

    public Task AppendAsync(HistoryRecord record, CancellationToken cancellationToken)
    {
        Records.Insert(0, record);
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
