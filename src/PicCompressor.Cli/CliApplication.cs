using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Engine.Guetzli;
using PicCompressor.Engine.Jpegli;
using PicCompressor.Infrastructure;
using PicCompressor.NativeInterop;

namespace PicCompressor.Cli;

internal static class CliApplication
{
    private const string Usage =
        """
        Usage: piccompressor <input> [<input> ...] [options]
          --engine <jpegli|guetzli>     Engine (default: jpegli; guetzli needs quality >= 84)
          --quality <1-100>             JPEG quality (default: 80)
          --output-dir <path>           Output directory
          --suffix <text>               Output suffix (default: _compressed)
          --collision <skip|rename|overwrite>
          --larger-output <discard|keep>
          --exif <keep|private|remove>  EXIF handling (default: remove)
          --color-profile <preserve|srgb|remove>
          --recursive                   Scan input directories recursively
          --dry-run                     Validate and plan without writing
          --parallelism <1-256>         Maximum concurrent jobs (default: 1)
          --json                        Emit schema-versioned JSON
          --no-history                  Do not record results in the local history
          --log <path>                  Write the JSONL log to this path
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
    private static void LogOutcome(
        IDiagnosticLog log,
        IReadOnlyList<CompressionJobPlan> plans,
        IReadOnlyList<CompressionExecutionResult> results,
        string? historyWarning)
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

        if (historyWarning is not null)
        {
            log.Write(
                new DiagnosticEntry(
                    now,
                    DiagnosticSeverity.Warning,
                    Component,
                    historyWarning));
        }
    }

    /// <summary>
    /// Records finished jobs in the shared local history. A persistence failure
    /// must not turn a correctly encoded image into a compression error, so it
    /// is reported as a separate warning instead (requirement 14.4).
    /// </summary>
    private static async Task<string?> RecordHistoryAsync(
        ICompressionHistoryStore? historyStore,
        IReadOnlyList<CompressionExecutionResult> results)
    {
        if (results.Count == 0)
        {
            return null;
        }

        try
        {
            var store = historyStore
                ?? new SqliteCompressionHistoryStore(
                    ApplicationDataPaths.HistoryDatabasePath);
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

        var log = diagnosticLog
            ?? new JsonLinesDiagnosticLog(
                options.LogPath ?? ApplicationDataPaths.DiagnosticLogPath);

        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var comparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var fileSystem = new PhysicalFileSystem(comparer);
            var inspector = new PhysicalInputImageInspector();
            var discoveredInputs = new PhysicalInputDiscovery(comparer).Discover(
                options.InputPaths,
                options.Recursive,
                options.OutputDirectory);
            if (discoveredInputs.Count == 0)
            {
                await WriteErrorAsync(
                    standardError,
                    options.Json,
                    3,
                    CompressionErrorCategory.InputNotFound.ToString(),
                    "No supported JPEG or PNG input was found.").ConfigureAwait(false);
                return 3;
            }

            var jobFactory = new CompressionJobFactory(
                fileSystem,
                inspector,
                new InputValidationLimits(500 * 1024 * 1024, 250_000_000),
                TimeProvider.System);
            var plans = new CompressionBatchPlanner(jobFactory).Plan(
                discoveredInputs,
                new CompressionBatchSettings(
                    BuildEngineSettings(options),
                    options.ExifPolicy,
                    options.ColorProfilePolicy,
                    RgbColor.White,
                    options.CollisionPolicy,
                    options.LargerOutputPolicy,
                    options.OutputDirectory,
                    options.Suffix));
            if (options.DryRun)
            {
                await WriteDryRunAsync(standardOutput, standardError, options.Json, plans)
                    .ConfigureAwait(false);
                return MapBatchExitCode(plans, []);
            }

            var bridge = new NativeCodecBridge(TimeProvider.System);
            var executor = new CompressionExecutor(
                [new JpegliEngineAdapter(bridge), new GuetzliEngineAdapter(bridge)],
                new SafeOutputPublisher(fileSystem, inspector));
            var jobs = plans
                .Where(plan => plan.Job is not null)
                .Select(plan => plan.Job!)
                .ToArray();
            var results = await new CompressionBatchExecutor(executor)
                .ExecuteAsync(jobs, options.Parallelism, cancellationSource.Token)
                .ConfigureAwait(false);
            var exitCode = MapBatchExitCode(plans, results);
            var historyWarning = options.NoHistory
                ? null
                : await RecordHistoryAsync(historyStore, results).ConfigureAwait(false);

            LogOutcome(log, plans, results, historyWarning);

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
                            historyWarning
                        },
                        JsonOptions))
                    .ConfigureAwait(false);
            }
            else
            {
                if (historyWarning is not null)
                {
                    await standardError.WriteLineAsync(historyWarning).ConfigureAwait(false);
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
            exception is IOException or UnauthorizedAccessException)
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

    private static int MapExitCode(CompressionExecutionResult result) =>
        result.Status is JobStatus.Succeeded
            ? 0
            : result.Status is JobStatus.Canceled
                ? 6
                : MapExitCode(result.ErrorCategory ?? CompressionErrorCategory.Unexpected);

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

    // Guetzli only exposes quality; Jpegli carries the chroma and progressive
    // defaults. The quality has already been validated against the engine floor
    // during option parsing.
    private static CompressionEngineSettings BuildEngineSettings(CliOptions options) =>
        options.EngineId == GuetzliSettings.GuetzliEngineId
            ? new GuetzliSettings(options.Quality)
            : new JpegliSettings(
                options.Quality,
                JpegliChromaSubsampling.Subsampling420,
                2);

    private static int MapBatchExitCode(
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
