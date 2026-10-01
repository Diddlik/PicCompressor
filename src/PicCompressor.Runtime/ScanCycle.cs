using System.Data.Common;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Infrastructure;

namespace PicCompressor.Runtime;

public sealed record ScanCycleRequest(
    IReadOnlyList<string> InputPaths,
    bool Recursive,
    CompressionBatchSettings Settings,
    string? StatePath,
    int StableForSeconds,
    int Parallelism,
    bool DryRun = false);

public sealed record ScanCycleResult(
    IReadOnlyList<CompressionJobPlan> Plans,
    IReadOnlyList<CompressionExecutionResult> Results,
    ScanSelection Selection,
    bool NoInputFound,
    string? HistoryWarning = null,
    string? StateWarning = null)
{
    public IEnumerable<string> Warnings =>
        new[] { HistoryWarning, StateWarning }.OfType<string>();
}

/// <summary>
/// Gemeinsamer Durchlauf für CLI und Web-Dienst: Discovery, Zustandsabgleich, Planung,
/// Kompression sowie persistenter Verlauf und Scan-Zustand.
/// </summary>
public sealed class ScanCycle(
    IFileSystem fileSystem,
    IInputImageInspector inspector,
    IInputDiscovery discovery,
    Lazy<CompressionExecutor> executor,
    StringComparer pathComparer,
    ICompressionHistoryStore? historyStore,
    bool recordHistory,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly CompressionJobFactory jobFactory = new(
        fileSystem,
        inspector,
        new InputValidationLimits(500 * 1024 * 1024, 250_000_000),
        timeProvider ?? TimeProvider.System);

    public async Task<ScanCycleResult> RunAsync(
        ScanCycleRequest request,
        CancellationToken cancellationToken)
    {
        var empty = new ScanSelection([], [], 0);
        var discoveredInputs = discovery.Discover(
            request.InputPaths,
            request.Recursive,
            request.Settings.OutputDirectory);
        if (discoveredInputs.Count == 0)
        {
            return new([], [], empty, NoInputFound: true);
        }

        var stateStore = request.StatePath is null ? null : new JsonScanStateStore(request.StatePath);
        var previousState = stateStore?.Load() ?? [];
        var fingerprint = IncrementalScan.Fingerprint(request.Settings);
        var selection = IncrementalScan.Select(
            discoveredInputs,
            previousState,
            fingerprint,
            TimeSpan.FromSeconds(request.StableForSeconds),
            clock.GetUtcNow(),
            fileSystem,
            pathComparer);
        var replaceableOutputs = new Dictionary<string, string>(pathComparer);
        foreach (var entry in previousState.Where(entry => entry.OutputPath.Length > 0))
        {
            replaceableOutputs[entry.InputPath] = entry.OutputPath;
        }

        var plans = new CompressionBatchPlanner(jobFactory).Plan(
            selection.Inputs,
            request.Settings,
            replaceableOutputs);
        if (request.DryRun)
        {
            return new(plans, [], selection, NoInputFound: false);
        }

        var jobs = plans
            .Where(plan => plan.Job is not null)
            .Select(plan => plan.Job!)
            .ToArray();
        var results = await new CompressionBatchExecutor(executor.Value)
            .ExecuteAsync(jobs, request.Parallelism, cancellationToken)
            .ConfigureAwait(false);
        var historyWarning = recordHistory
            ? await RecordHistoryAsync(results).ConfigureAwait(false)
            : null;
        var stateWarning = SaveScanState(
            stateStore,
            previousState,
            discoveredInputs,
            selection,
            results,
            fingerprint);

        return new(plans, results, selection, NoInputFound: false, historyWarning, stateWarning);
    }

    private async Task<string?> RecordHistoryAsync(IReadOnlyList<CompressionExecutionResult> results)
    {
        if (results.Count == 0)
        {
            return null;
        }

        try
        {
            var store = historyStore
                ?? new SqliteCompressionHistoryStore(ApplicationDataPaths.HistoryDatabasePath);
            foreach (var result in results)
            {
                await store.AppendAsync(
                    new CompressionHistoryEntry(
                        result.EndedAt,
                        Path.GetFileName(result.InputPath),
                        result.EngineId,
                        result.InputSizeBytes,
                        result.EncodedSizeBytes,
                        result.Status,
                        result.ErrorCategory),
                    CancellationToken.None).ConfigureAwait(false);
            }

            return null;
        }
        catch (Exception exception) when (
            exception is DbException
                or IOException
                or InvalidDataException
                or UnauthorizedAccessException)
        {
            return $"Results were not recorded in the history: {exception.Message}";
        }
    }

    private string? SaveScanState(
        JsonScanStateStore? stateStore,
        IReadOnlyList<ScanStateEntry> previousState,
        IReadOnlyList<DiscoveredInput> discoveredInputs,
        ScanSelection selection,
        IReadOnlyList<CompressionExecutionResult> results,
        string fingerprint)
    {
        if (stateStore is null)
        {
            return null;
        }

        var inputsByPath = new Dictionary<string, DiscoveredInput>(pathComparer);
        foreach (var input in discoveredInputs)
        {
            inputsByPath[input.Path] = input;
        }

        var current = new List<ScanStateEntry>(selection.Unchanged);
        foreach (var result in results.Where(result => result.Status is JobStatus.Succeeded))
        {
            if (inputsByPath.TryGetValue(result.InputPath, out var input))
            {
                current.Add(
                    new(
                        input.Path,
                        input.FileSizeBytes,
                        input.LastWriteTimeUtc.UtcTicks,
                        fingerprint,
                        result.OutputPublished ? result.OutputPath : ""));
            }
        }

        try
        {
            stateStore.Save(
                IncrementalScan.Merge(
                    previousState, discoveredInputs, current, fileSystem, pathComparer));
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"The scan state was not written: {exception.Message}";
        }
    }
}
