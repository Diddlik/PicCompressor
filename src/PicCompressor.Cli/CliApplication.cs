using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Engine.Jpegli;
using PicCompressor.Infrastructure;
using PicCompressor.NativeInterop;

namespace PicCompressor.Cli;

internal static class CliApplication
{
    private const string Usage =
        """
        Usage: piccompressor <input> [<input> ...] [options]
          --engine <jpegli>             Engine (default: jpegli)
          --quality <1-100>             JPEG quality (default: 80)
          --output-dir <path>           Output directory
          --suffix <text>               Output suffix (default: _compressed)
          --collision <skip|rename|overwrite>
          --larger-output <discard|keep>
          --exif <keep|private|remove>  EXIF handling (default: remove)
          --color-profile <preserve|srgb|remove>
          --recursive                   Scan input directories recursively
          --dry-run                     Validate and plan without writing
          --parallelism <1-256>         Maximum concurrent jobs (default: half the logical CPUs)
          --timeout <0-86400>           Encoder time limit in seconds (default: 0 = no limit)
          --min-savings <0-99>          Discard output saving less than this percent (default: 0)
          --json                        Emit schema-versioned JSON
          --no-history                  Do not record results in the local history
          --log <path>                  Write the JSONL log to this path
          --state <path>                Track processed inputs there; unchanged ones are a no-op
          --lock <path>                 Skip the run while another one holds this lock
          --stable-for <0-86400>        Skip inputs modified within this many seconds
          --config <path>               Watch the folders of this configuration file continuously
          --once                        With --config: run one cycle over every folder and exit
          --help                        Show help
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Writes the structured record of this run. Only file names are logged;
    /// full paths count as unnecessary detail (requirement 13.3).
    /// </summary>
    internal static void LogOutcome(
        IDiagnosticLog log,
        IReadOnlyList<CompressionJobPlan> plans,
        IReadOnlyList<CompressionExecutionResult> results,
        IReadOnlyList<string> warnings)
    {
        const string Component = "Cli";
        var now = DateTimeOffset.UtcNow;

        foreach (var plan in plans.Where(plan => plan.ErrorCategory is not null))
        {
            log.Write(
                new DiagnosticEntry(
                    now,
                    DiagnosticSeverity.Error,
                    Component,
                    "Input was rejected during planning.",
                    Path.GetFileName(plan.Input.Path),
                    ErrorCategory: plan.ErrorCategory));
        }

        foreach (var result in results)
        {
            log.Write(
                new DiagnosticEntry(
                    now,
                    result.Status is JobStatus.Succeeded
                        ? DiagnosticSeverity.Information
                        : DiagnosticSeverity.Error,
                    Component,
                    result.Status is JobStatus.Succeeded
                        ? result.OutputPublished
                            ? "Job succeeded and the output was published."
                            : "Job succeeded without publishing an output."
                        : "Job did not succeed.",
                    Path.GetFileName(result.InputPath),
                    result.JobId,
                    result.ErrorCategory));
        }

        foreach (var warning in warnings)
        {
            log.Write(
                new DiagnosticEntry(
                    now,
                    DiagnosticSeverity.Warning,
                    Component,
                    warning));
        }
    }

    internal static async Task<int> RunAsync(
        string[] args,
        TextWriter standardOutput,
        TextWriter standardError,
        ICompressionHistoryStore? historyStore = null,
        IDiagnosticLog? diagnosticLog = null)
    {
        if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            await standardOutput.WriteLineAsync(Usage).ConfigureAwait(false);
            return 0;
        }

        var json = args.Contains("--json", StringComparer.Ordinal);
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (CliUsageException exception)
        {
            await WriteErrorAsync(standardError, json, 2, "InvalidArguments", exception.Message)
                .ConfigureAwait(false);
            return 2;
        }

        using var cancellationSource = new CancellationTokenSource();
        // Das erste Signal löst den geordneten Abbruch aus, ein zweites überlässt das Beenden
        // wieder dem Betriebssystem (Abschnitt 12). SIGTERM ist der Weg, auf dem ein Container
        // gestoppt wird (D-057).
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = !cancellationSource.IsCancellationRequested;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        using var terminationSignal = CreateTerminationSignal(cancellationSource);

        try
        {
            return options.ConfigPath is null
                ? await RunSingleAsync(
                        options,
                        standardOutput,
                        standardError,
                        historyStore,
                        diagnosticLog,
                        cancellationSource.Token)
                    .ConfigureAwait(false)
                : await RunConfiguredAsync(
                        options,
                        standardOutput,
                        standardError,
                        historyStore,
                        diagnosticLog,
                        cancellationSource.Token)
                    .ConfigureAwait(false);
        }
        catch (JobCreationException exception)
        {
            var exitCode = MapExitCode(exception.Category);
            await WriteErrorAsync(
                standardError,
                options.Json,
                exitCode,
                exception.Category.ToString(),
                exception.Message).ConfigureAwait(false);
            return exitCode;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await WriteErrorAsync(
                standardError,
                options.Json,
                2,
                CompressionErrorCategory.InvalidArguments.ToString(),
                exception.Message).ConfigureAwait(false);
            return 2;
        }
        catch (FileNotFoundException exception)
        {
            await WriteErrorAsync(
                standardError,
                options.Json,
                3,
                CompressionErrorCategory.InputNotFound.ToString(),
                exception.Message).ConfigureAwait(false);
            return 3;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await WriteErrorAsync(
                standardError,
                options.Json,
                8,
                CompressionErrorCategory.FileSystemError.ToString(),
                exception.Message).ConfigureAwait(false);
            return 8;
        }
        catch (Exception exception)
        {
            await WriteErrorAsync(
                standardError,
                options.Json,
                1,
                CompressionErrorCategory.Unexpected.ToString(),
                exception.Message).ConfigureAwait(false);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// Ein einzelner Lauf über die Eingaben der Befehlszeile.
    /// </summary>
    private static async Task<int> RunSingleAsync(
        CliOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        ICompressionHistoryStore? historyStore,
        IDiagnosticLog? diagnosticLog,
        CancellationToken cancellationToken)
    {
        var log = diagnosticLog
            ?? new JsonLinesDiagnosticLog(
                options.LogPath ?? ApplicationDataPaths.DiagnosticLogPath);

        // Ein zweiter gleichzeitiger Lauf ist ein erfolgreicher No-op statt eines zweiten
        // Workerpools (MP-005). Der Lock wird beim Prozessende freigegeben, auch nach Absturz.
        using var runLock = options.LockPath is null ? null : RunLock.TryAcquire(options.LockPath);
        if (options.LockPath is not null && runLock is null)
        {
            await standardOutput.WriteLineAsync(
                options.Json
                    ? JsonSerializer.Serialize(
                        new { schemaVersion = 1, skippedBecauseLocked = true },
                        JsonOptions)
                    : "Another run holds the lock; nothing to do.").ConfigureAwait(false);
            return 0;
        }

        var comparer = PathComparer;
        var fileSystem = new PhysicalFileSystem(comparer);
        var inspector = new PhysicalInputImageInspector();
        var settings = new CompressionBatchSettings(
            BuildEngineSettings(options),
            options.ExifPolicy,
            options.ColorProfilePolicy,
            RgbColor.White,
            options.CollisionPolicy,
            options.LargerOutputPolicy,
            options.OutputDirectory,
            options.Suffix,
            options.MinimumSavingsPercent);
        var cycle = new ScanCycle(
            fileSystem,
            inspector,
            new PhysicalInputDiscovery(comparer),
            CreateExecutor(fileSystem, inspector, options.EngineId, options.TimeoutSeconds),
            comparer,
            historyStore,
            !options.NoHistory);

        var cycleResult = await cycle.RunAsync(
            new(
                options.InputPaths,
                options.Recursive,
                settings,
                options.StatePath,
                options.StableForSeconds,
                options.Parallelism,
                options.DryRun),
            cancellationToken).ConfigureAwait(false);
        if (cycleResult.NoInputFound)
        {
            await WriteErrorAsync(
                standardError,
                options.Json,
                3,
                CompressionErrorCategory.InputNotFound.ToString(),
                "No supported JPEG or PNG input was found.").ConfigureAwait(false);
            return 3;
        }

        var plans = cycleResult.Plans;
        var results = cycleResult.Results;
        if (options.DryRun)
        {
            await WriteDryRunAsync(standardOutput, standardError, options.Json, plans)
                .ConfigureAwait(false);
            return MapBatchExitCode(plans, []);
        }

        var exitCode = MapBatchExitCode(plans, results);
        var warnings = cycleResult.Warnings.ToArray();
        LogOutcome(log, plans, results, warnings);

        if (options.Json)
        {
            await standardOutput.WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        result = results.Count == 1 && plans.All(plan => plan.Job is not null)
                            ? results[0]
                            : null,
                        results,
                        planningErrors = plans
                            .Where(plan => plan.ErrorCategory is not null)
                            .Select(ToPlanOutput),
                        unchangedCount = cycleResult.Selection.Unchanged.Count,
                        unstableCount = cycleResult.Selection.UnstableCount,
                        historyWarning = cycleResult.HistoryWarning,
                        stateWarning = cycleResult.StateWarning
                    },
                    JsonOptions))
                .ConfigureAwait(false);
        }
        else
        {
            foreach (var warning in warnings)
            {
                await standardError.WriteLineAsync(warning).ConfigureAwait(false);
            }

            if (cycleResult.Selection.Unchanged.Count > 0)
            {
                await standardOutput.WriteLineAsync(
                    $"Unchanged: {cycleResult.Selection.Unchanged.Count}").ConfigureAwait(false);
            }

            if (cycleResult.Selection.UnstableCount > 0)
            {
                await standardOutput.WriteLineAsync(
                    $"Still changing, retried later: {cycleResult.Selection.UnstableCount}")
                    .ConfigureAwait(false);
            }

            foreach (var plan in plans.Where(plan => plan.ErrorCategory is not null))
            {
                await standardError.WriteLineAsync(
                    $"{plan.Input.Path}: {plan.ErrorCategory}: {plan.ErrorText}")
                    .ConfigureAwait(false);
            }

            foreach (var result in results)
            {
                if (result.Status is JobStatus.Succeeded)
                {
                    var message = result.OutputPublished
                        ? $"Compressed: {result.OutputPath}"
                        : result.Warning!;
                    await standardOutput.WriteLineAsync(message).ConfigureAwait(false);
                }
                else
                {
                    await standardError.WriteLineAsync(
                        $"{result.InputPath}: {result.ErrorCategory}: {result.ErrorText}")
                        .ConfigureAwait(false);
                }
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Der konfigurationsgesteuerte Dauerbetrieb über mehrere überwachte Ordner (D-055, D-056).
    /// </summary>
    internal static async Task<int> RunConfiguredAsync(
        CliOptions options,
        TextWriter standardOutput,
        TextWriter standardError,
        ICompressionHistoryStore? historyStore,
        IDiagnosticLog? diagnosticLog,
        CancellationToken cancellationToken)
    {
        var comparer = PathComparer;
        ScanConfigurationResolution resolution;
        try
        {
            resolution = ScanConfigurationResolver.Resolve(
                new JsonScanConfigurationStore(options.ConfigPath!).Load(),
                ApplicationDataPaths.ApplicationDataDirectory,
                comparer);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or InvalidDataException)
        {
            // Eine fehlende oder fehlerhafte Konfiguration ist ein Nutzungsfehler, kein
            // Eingabe- oder Dateisystemfehler.
            await WriteErrorAsync(
                standardError,
                options.Json,
                2,
                CompressionErrorCategory.InvalidArguments.ToString(),
                exception.Message).ConfigureAwait(false);
            return 2;
        }

        if (resolution.Configuration is null)
        {
            await WriteErrorAsync(
                standardError,
                options.Json,
                2,
                CompressionErrorCategory.InvalidArguments.ToString(),
                string.Join(" ", resolution.Errors)).ConfigureAwait(false);
            return 2;
        }

        var configuration = resolution.Configuration;
        var log = diagnosticLog
            ?? new JsonLinesDiagnosticLog(
                configuration.LogPath ?? ApplicationDataPaths.DiagnosticLogPath);

        // Ein Lock für die gesamte Laufzeit: ein zweiter Container oder ein manueller Scan auf
        // denselben Ordnern läuft nicht parallel (D-057).
        using var runLock = configuration.LockPath is null
            ? null
            : RunLock.TryAcquire(configuration.LockPath);
        if (configuration.LockPath is not null && runLock is null)
        {
            await standardOutput.WriteLineAsync(
                options.Json
                    ? JsonSerializer.Serialize(
                        new { schemaVersion = 1, skippedBecauseLocked = true },
                        JsonOptions)
                    : "Another run holds the lock; nothing to do.").ConfigureAwait(false);
            return 0;
        }

        var fileSystem = new PhysicalFileSystem(comparer);
        var inspector = new PhysicalInputImageInspector();
        var cycle = new ScanCycle(
            fileSystem,
            inspector,
            new PhysicalInputDiscovery(comparer),
            CreateExecutor(
                fileSystem,
                inspector,
                JpegliSettings.JpegliEngineId,
                configuration.TimeoutSeconds),
            comparer,
            historyStore,
            configuration.History);

        return await new ScanService(
                configuration,
                cycle,
                log,
                standardOutput,
                standardError,
                options.Json,
                comparer)
            .RunAsync(options.Once, cancellationToken)
            .ConfigureAwait(false);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Der Executor wird erst beim ersten wirklichen Encoding gebaut, damit ein <c>--dry-run</c>
    /// die native Bibliothek nicht lädt. Enginespezifisches Zeitlimit (MP-004): 0 = kein Limit.
    /// </summary>
    private static Lazy<CompressionExecutor> CreateExecutor(
        PhysicalFileSystem fileSystem,
        PhysicalInputImageInspector inspector,
        string engineId,
        int timeoutSeconds) =>
        new(() => new CompressionExecutor(
            [new JpegliEngineAdapter(new NativeCodecBridge(TimeProvider.System))],
            new SafeOutputPublisher(fileSystem, inspector),
            TimeProvider.System,
            EngineRuntimeLimits.FromSeconds((engineId, timeoutSeconds))));

    /// <summary>
    /// Meldet <c>SIGTERM</c> an denselben geordneten Abbruch wie <c>Ctrl+C</c>; ein zweites Signal
    /// überlässt das Beenden dem Betriebssystem (D-057). Plattformen ohne diese Signale laufen
    /// unverändert weiter.
    /// </summary>
    private static IDisposable? CreateTerminationSignal(CancellationTokenSource cancellationSource)
    {
        try
        {
            return PosixSignalRegistration.Create(
                PosixSignal.SIGTERM,
                context =>
                {
                    context.Cancel = !cancellationSource.IsCancellationRequested;
                    cancellationSource.Cancel();
                });
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static int MapExitCode(CompressionErrorCategory category) =>
        category switch
        {
            CompressionErrorCategory.InvalidArguments => 2,
            CompressionErrorCategory.InputNotFound => 3,
            CompressionErrorCategory.EngineUnavailable => 7,
            CompressionErrorCategory.OutputValidationFailed
                or CompressionErrorCategory.OutputConflict
                or CompressionErrorCategory.FileSystemError => 8,
            CompressionErrorCategory.Canceled => 6,
            CompressionErrorCategory.Unexpected => 1,
            _ => 5
        };

    private static CompressionEngineSettings BuildEngineSettings(CliOptions options) =>
        new JpegliSettings(
            options.Quality,
            JpegliChromaSubsampling.Subsampling420,
            2);

    internal static int MapBatchExitCode(
        IReadOnlyList<CompressionJobPlan> plans,
        IReadOnlyList<CompressionExecutionResult> results)
    {
        var succeeded = results.Count(result => result.Status is JobStatus.Succeeded);
        var planned = plans.Count(plan => plan.Job is not null);
        var failed = plans.Count - planned
            + results.Count(result => result.Status is not JobStatus.Succeeded);
        if (failed == 0)
        {
            return 0;
        }

        if (succeeded > 0 || results.Count == 0 && planned > 0)
        {
            return 4;
        }

        var categories = plans
            .Select(plan => plan.ErrorCategory)
            .Concat(results.Select(result => result.ErrorCategory))
            .Where(category => category is not null)
            .Select(category => category!.Value)
            .ToArray();
        if (categories.Contains(CompressionErrorCategory.Canceled))
        {
            return 6;
        }

        if (categories.Any(category => category is
            CompressionErrorCategory.OutputValidationFailed
            or CompressionErrorCategory.OutputConflict
            or CompressionErrorCategory.FileSystemError))
        {
            return 8;
        }

        if (categories.Contains(CompressionErrorCategory.EngineUnavailable))
        {
            return 7;
        }

        if (categories.All(category => category is CompressionErrorCategory.InputNotFound))
        {
            return 3;
        }

        if (categories.Contains(CompressionErrorCategory.InvalidArguments))
        {
            return 2;
        }

        return 5;
    }

    private static async Task WriteDryRunAsync(
        TextWriter standardOutput,
        TextWriter standardError,
        bool json,
        IReadOnlyList<CompressionJobPlan> plans)
    {
        if (json)
        {
            await standardOutput.WriteLineAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        dryRun = true,
                        plans = plans.Select(ToPlanOutput)
                    },
                    JsonOptions)).ConfigureAwait(false);
            return;
        }

        foreach (var plan in plans)
        {
            if (plan.Job is not null)
            {
                await standardOutput.WriteLineAsync(
                    $"Planned: {plan.Input.Path} -> {plan.Job.OutputPath}").ConfigureAwait(false);
            }
            else
            {
                await standardError.WriteLineAsync(
                    $"{plan.Input.Path}: {plan.ErrorCategory}: {plan.ErrorText}")
                    .ConfigureAwait(false);
            }
        }
    }

    private static object ToPlanOutput(CompressionJobPlan plan) =>
        new
        {
            inputPath = plan.Input.Path,
            outputPath = plan.Job?.OutputPath,
            status = plan.Job is null ? "Failed" : "Planned",
            errorCategory = plan.ErrorCategory,
            errorText = plan.ErrorText
        };

    private static Task WriteErrorAsync(
        TextWriter writer,
        bool json,
        int exitCode,
        string category,
        string message) =>
        writer.WriteLineAsync(
            json
                ? JsonSerializer.Serialize(
                    new { schemaVersion = 1, error = new { exitCode, category, message } },
                    JsonOptions)
                : $"{category}: {message}");
}
