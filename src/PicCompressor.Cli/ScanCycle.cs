using System.Data.Common;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Infrastructure;

namespace PicCompressor.Cli;

internal sealed record ScanCycleRequest(
    IReadOnlyList<string> InputPaths,
    bool Recursive,
    CompressionBatchSettings Settings,
    string? StatePath,
    int StableForSeconds,
    int Parallelism,
    bool DryRun = false);

internal sealed record ScanCycleResult(
    IReadOnlyList<CompressionJobPlan> Plans,
    IReadOnlyList<CompressionExecutionResult> Results,
    ScanSelection Selection,
    bool NoInputFound,
    string? HistoryWarning = null,
    string? StateWarning = null)
{
    internal IEnumerable<string> Warnings =>
        new[] { HistoryWarning, StateWarning }.OfType<string>();
}

/// <summary>
/// Ein Durchlauf des wiederkehrenden Scans über eine Eingabe (MP-005): Discovery, Zustandsabgleich,
/// Planung, Ausführung, Verlauf und Zustand. Ein einzelner CLI-Lauf und jeder Zyklus des
/// Dauerbetriebs (D-056) verwenden dieselbe Methode; es gibt keinen zweiten Verarbeitungspfad.
/// Die teuren Bestandteile — Dateisystem, Inspector, Executor und damit die native Bibliothek —
/// werden einmal aufgebaut und über die Lebensdauer des Prozesses wiederverwendet.
/// </summary>
internal sealed class ScanCycle(
    IFileSystem fileSystem,
    PhysicalInputImageInspector inspector,
    IInputDiscovery discovery,
    Lazy<CompressionExecutor> executor,
    StringComparer pathComparer,
    ICompressionHistoryStore? historyStore,
    bool recordHistory)
{
    private readonly CompressionJobFactory jobFactory = new(
        fileSystem,
        inspector,
        new InputValidationLimits(500 * 1024 * 1024, 250_000_000),
        TimeProvider.System);

    internal async Task<ScanCycleResult> RunAsync(
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

        // Wiederkehrender Scan (MP-005): unveränderte Eingaben mit gültiger Ausgabe werden
        // übersprungen, noch wachsende Dateien bleiben für einen späteren Lauf liegen.
        var stateStore = request.StatePath is null ? null : new JsonScanStateStore(request.StatePath);
        var previousState = stateStore?.Load() ?? [];
        var fingerprint = IncrementalScan.Fingerprint(request.Settings);
        var selection = IncrementalScan.Select(
            discoveredInputs,
            previousState,
            fingerprint,
            TimeSpan.FromSeconds(request.StableForSeconds),
            DateTimeOffset.UtcNow,
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

    /// <summary>
    /// Records finished jobs in the shared local history. A persistence failure
    /// must not turn a correctly encoded image into a compression error, so it
    /// is reported as a separate warning instead (requirement 14.4).
    /// </summary>
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
                // The history stores the file name only; absolute paths count as
                // potentially sensitive data (requirement 13.1).
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

    /// <summary>
    /// Schreibt den Zustand des wiederkehrenden Scans (MP-005). Aufgezeichnet werden die
    /// unveränderten Eingaben dieses Laufs und jeder erfolgreiche Job; ein Erfolg ohne Ausgabe
    /// wird mit leerem Ausgabepfad vermerkt, damit er nicht in jedem Lauf erneut kodiert wird.
    /// Ein Schreibfehler ist wie beim Verlauf eine eigene Warnung und kein Kompressionsfehler
    /// (Abschnitt 14.4).
    /// </summary>
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
