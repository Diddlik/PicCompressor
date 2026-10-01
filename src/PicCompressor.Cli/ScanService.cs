using System.Text.Json;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Infrastructure;
using PicCompressor.Runtime;

namespace PicCompressor.Cli;

/// <summary>
/// Der konfigurationsgesteuerte Dauerbetrieb (D-056): eine Schleife um <see cref="ScanCycle"/>,
/// getaktet durch ein Intervall oder durch entprellte Dateisystemereignisse. Der Watch-Modus
/// liefert nur den Auslöser, nie eine eigene Dateiliste — ein Ereignis startet den vollständigen
/// inkrementellen Scan seines Ordners, sodass Ausschlüsse, Symlinks und selbst erzeugte Ausgaben
/// genau einer Regelmenge folgen. Ein Fehler in einem Zyklus beendet die Überwachung nicht
/// (MP-005).
/// </summary>
internal sealed class ScanService(
    ResolvedScanConfiguration configuration,
    ScanCycle cycle,
    IDiagnosticLog log,
    TextWriter standardOutput,
    TextWriter standardError,
    bool json,
    StringComparer pathComparer)
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    private readonly Lock gate = new();
    private readonly HashSet<string> pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim signal = new(0, 1);
    private long eventCount;

    /// <param name="once">
    /// Genau einen Zyklus über alle Ordner ausführen und beenden — der Weg für einen externen
    /// Zeitplaner und für Tests.
    /// </param>
    internal async Task<int> RunAsync(bool once, CancellationToken cancellationToken)
    {
        var watchers = configuration.Mode is ScanMode.Watch && !once
            ? StartWatchers()
            : [];
        var exitCode = 0;
        try
        {
            MarkAll();
            while (!cancellationToken.IsCancellationRequested)
            {
                string[] due;
                lock (gate)
                {
                    due = [.. pending];
                    pending.Clear();
                }

                foreach (var name in due)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var folder = configuration.Folders.First(
                        candidate => candidate.Name == name);
                    var code = await RunFolderAsync(folder, cancellationToken)
                        .ConfigureAwait(false);
                    if (exitCode == 0)
                    {
                        exitCode = code;
                    }
                }

                if (once)
                {
                    return exitCode;
                }

                await WaitForNextCycleAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Ein geordnetes Ende (Ctrl+C, SIGTERM) ist kein Fehler des Dauerbetriebs.
        }
        finally
        {
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }
        }

        return once ? exitCode : 0;
    }

    private async Task<int> RunFolderAsync(
        ResolvedScanFolder folder,
        CancellationToken cancellationToken)
    {
        ScanCycleResult result;
        try
        {
            result = await cycle.RunAsync(
                new(
                    [folder.InputPath],
                    folder.Recursive,
                    folder.Settings,
                    folder.StatePath,
                    configuration.StableForSeconds,
                    configuration.Parallelism),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JobCreationException
                or ArgumentException
                or NotSupportedException)
        {
            // Ein einzelner Ordner darf die Überwachung nicht beenden: der Fehler wird gemeldet,
            // der nächste Zyklus versucht es erneut (MP-005).
            log.Write(
                new DiagnosticEntry(
                    DateTimeOffset.UtcNow,
                    DiagnosticSeverity.Error,
                    "ScanService",
                    $"Scan of folder '{folder.Name}' failed: {exception.Message}"));
            await standardError.WriteLineAsync($"{folder.Name}: {exception.Message}")
                .ConfigureAwait(false);
            return 8;
        }

        CliApplication.LogOutcome(log, result.Plans, result.Results, [.. result.Warnings]);
        await WriteFolderResultAsync(folder, result).ConfigureAwait(false);

        // Ein Ordner ohne passende Datei ist im Dauerbetrieb ein Normalzustand und kein Fehler;
        // der einzelne CLI-Lauf meldet dafür weiterhin Exit-Code 3.
        return result.NoInputFound ? 0 : CliApplication.MapBatchExitCode(result.Plans, result.Results);
    }

    private async Task WriteFolderResultAsync(ResolvedScanFolder folder, ScanCycleResult result)
    {
        var succeeded = result.Results.Count(item => item.Status is JobStatus.Succeeded);
        var failed = result.Plans.Count(plan => plan.Job is null)
            + result.Results.Count(item => item.Status is not JobStatus.Succeeded);
        if (json)
        {
            await standardOutput.WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        folder = folder.Name,
                        succeeded,
                        failed,
                        unchangedCount = result.Selection.Unchanged.Count,
                        unstableCount = result.Selection.UnstableCount,
                        historyWarning = result.HistoryWarning,
                        stateWarning = result.StateWarning
                    })).ConfigureAwait(false);
            return;
        }

        foreach (var warning in result.Warnings)
        {
            await standardError.WriteLineAsync($"{folder.Name}: {warning}").ConfigureAwait(false);
        }

        foreach (var plan in result.Plans.Where(plan => plan.ErrorCategory is not null))
        {
            await standardError.WriteLineAsync(
                $"{folder.Name}: {plan.Input.Path}: {plan.ErrorCategory}: {plan.ErrorText}")
                .ConfigureAwait(false);
        }

        foreach (var item in result.Results.Where(item => item.Status is not JobStatus.Succeeded))
        {
            await standardError.WriteLineAsync(
                $"{folder.Name}: {item.InputPath}: {item.ErrorCategory}: {item.ErrorText}")
                .ConfigureAwait(false);
        }

        if (succeeded > 0 || failed > 0)
        {
            await standardOutput.WriteLineAsync(
                $"{folder.Name}: compressed {succeeded}, failed {failed}, "
                + $"unchanged {result.Selection.Unchanged.Count}, "
                + $"still changing {result.Selection.UnstableCount}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wartet auf den nächsten Zyklus. Im Intervallmodus ist das die Wartezeit, im Watch-Modus das
    /// erste entprellte Ereignis — spätestens aber die Wartezeit, weil <c>inotify</c> auf
    /// eingehängten Netzfreigaben keine Ereignisse liefert.
    /// </summary>
    private async Task WaitForNextCycleAsync(CancellationToken cancellationToken)
    {
        if (configuration.Mode is ScanMode.Interval)
        {
            await Task.Delay(configuration.Interval, cancellationToken).ConfigureAwait(false);
            MarkAll();
            return;
        }

        if (!await signal.WaitAsync(configuration.Interval, cancellationToken).ConfigureAwait(false))
        {
            MarkAll();
            return;
        }

        long seen;
        do
        {
            lock (gate)
            {
                seen = eventCount;
            }

            await Task.Delay(configuration.Debounce, cancellationToken).ConfigureAwait(false);
        }
        while (Volatile.Read(ref eventCount) != seen);

        // Ereignisse, die während der Entprellung eintrafen, sind bereits vermerkt.
        await signal.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
    }

    private FileSystemWatcher[] StartWatchers() =>
        [.. configuration.Folders.Select(StartWatcher)];

    private FileSystemWatcher StartWatcher(ResolvedScanFolder folder)
    {
        var watcher = new FileSystemWatcher(folder.InputPath)
        {
            IncludeSubdirectories = folder.Recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Created += (_, eventArgs) => OnChanged(folder, eventArgs.FullPath);
        watcher.Changed += (_, eventArgs) => OnChanged(folder, eventArgs.FullPath);
        watcher.Renamed += (_, eventArgs) => OnChanged(folder, eventArgs.FullPath);
        // Ein übergelaufener Puffer verliert Ereignisse; der vollständige Scan holt sie ein.
        watcher.Error += (_, _) => Signal(folder.Name);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnChanged(ResolvedScanFolder folder, string path)
    {
        // Die eigene Ausgabe darf keinen weiteren Zyklus auslösen (MP-005).
        if (folder.OutputDirectory is not null && IsUnder(path, folder.OutputDirectory))
        {
            return;
        }

        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            return;
        }

        Signal(folder.Name);
    }

    private bool IsUnder(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathFullyQualified(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && pathComparer.Equals(
                Path.GetFullPath(Path.Combine(directory, relative)),
                Path.GetFullPath(path));
    }

    private void Signal(string folderName)
    {
        lock (gate)
        {
            pending.Add(folderName);
            eventCount++;
        }

        try
        {
            signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ein bereits gesetztes Signal genügt; der Zyklus liest die Menge der Ordner.
        }
    }

    private void MarkAll()
    {
        lock (gate)
        {
            foreach (var folder in configuration.Folders)
            {
                pending.Add(folder.Name);
            }
        }
    }
}
