using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Engine.Jpegli;
using PicCompressor.Infrastructure;
using PicCompressor.NativeInterop;
using PicCompressor.Runtime;

namespace PicCompressor.Web;

/// <summary>
/// Führt beim Start sofort und danach per Intervall, Dateisystemereignis oder Web-Trigger einen
/// vollständigen inkrementellen Scan aus. Ein einzelner Worker verhindert überlappende Läufe.
/// </summary>
public sealed class ScanWorker(
    WebConfigurationService configurations,
    ScanControl control,
    IConfiguration applicationConfiguration,
    ILogger<ScanWorker> logger)
    : BackgroundService
{
    private readonly string stateRoot = Path.GetFullPath(
        applicationConfiguration["PicCompressor:StateRoot"] ?? "/state");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(stateRoot);
        RunLock? runLock = null;
        string? lockedPath = null;
        try
        {
            var trigger = "startup";
            while (!stoppingToken.IsCancellationRequested)
            {
                ResolvedScanConfiguration? configuration = null;
                IDisposable? watchers = null;
                try
                {
                    var loadedConfigurationVersion = control.ConfigurationVersion;
                    var document = configurations.Load();
                    configuration = configurations.Validate(document.Configuration).Configuration!;
                    if (!SamePath(lockedPath, configuration.LockPath))
                    {
                        runLock?.Dispose();
                        runLock = null;
                        lockedPath = null;
                    }

                    if (configuration.LockPath is not null && runLock is null)
                    {
                        runLock = RunLock.TryAcquire(configuration.LockPath);
                        if (runLock is not null)
                        {
                            lockedPath = configuration.LockPath;
                        }
                    }

                    watchers = configuration.Mode is ScanMode.Watch
                        ? StartWatchers(configuration)
                        : null;
                    if (configuration.LockPath is not null && runLock is null)
                    {
                        control.Update(
                            ScanStatus.Idle with
                            {
                                State = "blocked",
                                LastError = "Another PicCompressor instance holds the configured scan lock."
                            });
                    }
                    else
                    {
                        if (string.Equals(trigger, "watch", StringComparison.Ordinal))
                        {
                            trigger = await DebounceAsync(configuration.Debounce, stoppingToken)
                                .ConfigureAwait(false);
                            if (string.Equals(trigger, "configuration", StringComparison.Ordinal))
                            {
                                watchers?.Dispose();
                                watchers = null;
                                continue;
                            }
                        }

                        configuration = configurations.Validate(document.Configuration).Configuration!;
                        if (!control.TryBeginScan(loadedConfigurationVersion))
                        {
                            watchers?.Dispose();
                            watchers = null;
                            trigger = "configuration";
                            continue;
                        }

                        await RunCycleAsync(configuration, trigger, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    watchers?.Dispose();
                    watchers = null;
                    break;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    logger.LogError(exception, "PicCompressor scan cycle failed.");
                    control.Update(
                        control.Status with
                        {
                            State = "error",
                            CurrentFolder = null,
                            FinishedAt = DateTimeOffset.UtcNow,
                            LastError = SafeMessage(exception)
                        });
                }

                var interval = configuration?.Interval ?? TimeSpan.FromSeconds(30);
                var nextRun = DateTimeOffset.UtcNow + interval;
                var waitingState = control.Status.State is "error" or "blocked"
                    ? control.Status.State
                    : "idle";
                control.Update(
                    control.Status with
                    {
                        State = waitingState,
                        NextRunAt = nextRun,
                        CurrentFolder = null
                    });

                try
                {
                    trigger = await WaitForTriggerAsync(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                finally
                {
                    watchers?.Dispose();
                }
            }
        }
        finally
        {
            runLock?.Dispose();
        }

        control.Update(control.Status with { State = "stopped", CurrentFolder = null, NextRunAt = null });
    }

    private async Task<string> WaitForTriggerAsync(
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(interval, waitCancellation.Token);
        var signal = control.WaitAsync(waitCancellation.Token).AsTask();
        var completed = await Task.WhenAny(delay, signal).ConfigureAwait(false);
        await waitCancellation.CancelAsync().ConfigureAwait(false);
        if (completed == signal)
        {
            var source = await signal.ConfigureAwait(false);
            await IgnoreCancellationAsync(delay).ConfigureAwait(false);
            return source;
        }

        await IgnoreCancellationAsync(signal).ConfigureAwait(false);
        return "interval";
    }

    private async Task<string> DebounceAsync(
        TimeSpan debounce,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var debounceCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(debounce, debounceCancellation.Token);
            var signal = control.WaitAsync(debounceCancellation.Token).AsTask();
            var completed = await Task.WhenAny(delay, signal).ConfigureAwait(false);
            await debounceCancellation.CancelAsync().ConfigureAwait(false);
            if (completed == delay)
            {
                await IgnoreCancellationAsync(signal).ConfigureAwait(false);
                return "watch";
            }

            var source = await signal.ConfigureAwait(false);
            await IgnoreCancellationAsync(delay).ConfigureAwait(false);
            if (!string.Equals(source, "watch", StringComparison.Ordinal))
            {
                return source;
            }
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool SamePath(string? left, string? right) => string.Equals(
        left,
        right,
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

    private async Task RunCycleAsync(
        ResolvedScanConfiguration configuration,
        string trigger,
        CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var fileSystem = new PhysicalFileSystem(comparer);
        var inspector = new PhysicalInputImageInspector();
        var executor = new Lazy<CompressionExecutor>(
            () => new CompressionExecutor(
                [new JpegliEngineAdapter(new NativeCodecBridge(TimeProvider.System))],
                new SafeOutputPublisher(fileSystem, inspector),
                TimeProvider.System,
                EngineRuntimeLimits.FromSeconds(
                    (JpegliSettings.JpegliEngineId, configuration.TimeoutSeconds))));
        var history = configuration.History
            ? new SqliteCompressionHistoryStore(Path.Combine(stateRoot, "history.db"))
            : null;
        var cycle = new ScanCycle(
            fileSystem,
            inspector,
            new PhysicalInputDiscovery(comparer),
            executor,
            comparer,
            history,
            configuration.History);

        var started = DateTimeOffset.UtcNow;
        var succeeded = 0;
        var failed = 0;
        var unchanged = 0;
        var unstable = 0;
        string? lastError = null;
        control.Update(
            new(
                "running",
                null,
                started,
                null,
                null,
                trigger,
                0,
                0,
                0,
                0,
                null));

        foreach (var folder in configuration.Folders)
        {
            control.Update(control.Status with { CurrentFolder = folder.Name });
            try
            {
                var result = await cycle.RunAsync(
                    new(
                        [folder.InputPath],
                        folder.Recursive,
                        folder.Settings,
                        folder.StatePath,
                        configuration.StableForSeconds,
                        configuration.Parallelism),
                    cancellationToken).ConfigureAwait(false);
                succeeded += result.Results.Count(item => item.Status is JobStatus.Succeeded);
                failed += result.Plans.Count(plan => plan.Job is null)
                    + result.Results.Count(item => item.Status is not JobStatus.Succeeded);
                unchanged += result.Selection.Unchanged.Count;
                unstable += result.Selection.UnstableCount;
                lastError = result.Warnings.LastOrDefault() ?? lastError;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or JobCreationException
                    or ArgumentException
                    or NotSupportedException)
            {
                failed++;
                lastError = SafeMessage(exception);
                logger.LogWarning(exception, "Scan of folder {Folder} failed.", folder.Name);
            }
        }

        control.Update(
            control.Status with
            {
                State = "idle",
                CurrentFolder = null,
                FinishedAt = DateTimeOffset.UtcNow,
                Succeeded = succeeded,
                Failed = failed,
                Unchanged = unchanged,
                Unstable = unstable,
                LastError = lastError
            });
    }

    private IDisposable StartWatchers(ResolvedScanConfiguration configuration)
    {
        var watchers = new List<FileSystemWatcher>();
        foreach (var folder in configuration.Folders)
        {
            if (!Directory.Exists(folder.InputPath))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(folder.InputPath)
            {
                IncludeSubdirectories = folder.Recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler changed = (_, eventArgs) => SignalIfImage(eventArgs.FullPath);
            RenamedEventHandler renamed = (_, eventArgs) => SignalIfImage(eventArgs.FullPath);
            watcher.Created += changed;
            watcher.Changed += changed;
            watcher.Renamed += renamed;
            watchers.Add(watcher);
        }

        return new WatcherScope(watchers);
    }

    private void SignalIfImage(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            control.Signal("watch");
        }
    }

    private static string SafeMessage(Exception exception) => exception switch
    {
        ConfigurationValidationException validation => string.Join(" ", validation.Errors),
        _ => exception.Message
    };

    private sealed class WatcherScope(IReadOnlyList<FileSystemWatcher> watchers) : IDisposable
    {
        public void Dispose()
        {
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }
        }
    }
}
