using System.Globalization;
using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed record CompressionExecutionResult(
    Guid JobId,
    JobStatus Status,
    string InputPath,
    string OutputPath,
    string EngineId,
    string? EngineVersion,
    long InputSizeBytes,
    long? EncodedSizeBytes,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool OutputValidated,
    bool OutputPublished,
    string? Warning,
    CompressionErrorCategory? ErrorCategory,
    string? ErrorText)
{
    public TimeSpan Duration => EndedAt - StartedAt;

    public long? SavedBytes => EncodedSizeBytes is long size
        ? InputSizeBytes - size
        : null;

    public double? SavedPercent => SavedBytes is long saved && InputSizeBytes > 0
        ? saved * 100d / InputSizeBytes
        : null;
}

public interface ICompressionJobExecutor
{
    Task<CompressionExecutionResult> ExecuteAsync(
        CompressionJob job,
        CancellationToken cancellationToken);
}

public sealed class CompressionExecutor : ICompressionJobExecutor
{
    private readonly IReadOnlyDictionary<string, ICompressionEngine> engines;
    private readonly SafeOutputPublisher outputPublisher;
    private readonly TimeProvider clock;
    private readonly EngineRuntimeLimits runtimeLimits;

    /// <summary>Bequemlichkeit für einen einzelnen Encoder; delegiert an die Mehr-Engine-Variante.</summary>
    public CompressionExecutor(
        ICompressionEngine engine,
        SafeOutputPublisher outputPublisher,
        TimeProvider? timeProvider = null,
        EngineRuntimeLimits? runtimeLimits = null)
        : this([engine], outputPublisher, timeProvider, runtimeLimits)
    {
    }

    /// <summary>
    /// Wählt pro Job die Engine anhand von <see cref="CompressionEngineSettings.EngineId"/>.
    /// Ein Job für eine nicht verdrahtete Engine schlägt als
    /// <see cref="CompressionErrorCategory.EngineUnavailable"/> fehl statt still auf eine andere
    /// Engine auszuweichen (Abschnitt 4.2). Ein optionales enginespezifisches Zeitlimit
    /// (<paramref name="runtimeLimits"/>) bricht ein hängendes Encoding kooperativ ab und meldet
    /// es als <see cref="CompressionErrorCategory.LimitExceeded"/> (MP-004, Abschnitt 7.1).
    /// </summary>
    public CompressionExecutor(
        IEnumerable<ICompressionEngine> engines,
        SafeOutputPublisher outputPublisher,
        TimeProvider? timeProvider = null,
        EngineRuntimeLimits? runtimeLimits = null)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(outputPublisher);
        this.outputPublisher = outputPublisher;
        clock = timeProvider ?? TimeProvider.System;
        this.runtimeLimits = runtimeLimits ?? EngineRuntimeLimits.None;

        var map = new Dictionary<string, ICompressionEngine>(StringComparer.Ordinal);
        foreach (var engine in engines)
        {
            ArgumentNullException.ThrowIfNull(engine);
            if (!map.TryAdd(engine.EngineId, engine))
            {
                throw new ArgumentException(
                    $"Duplicate engine ID: {engine.EngineId}",
                    nameof(engines));
            }
        }

        if (map.Count == 0)
        {
            throw new ArgumentException("At least one engine is required.", nameof(engines));
        }

        this.engines = map;
    }

    public async Task<CompressionExecutionResult> ExecuteAsync(
        CompressionJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var startedAt = clock.GetUtcNow();
        var engineId = job.EngineSettings.EngineId;

        // Eine bereits von PicCompressor markierte Eingabe wird nicht erneut komprimiert
        // (Issue #1): erfolgreicher Job ohne neue Ausgabe mit Hinweis, kein Encoding, keine
        // temporäre Datei.
        if (job.InputImageInfo.AlreadyOptimized)
        {
            return Skipped(job, startedAt);
        }

        if (!engines.TryGetValue(engineId, out var engine))
        {
            return Failure(
                job,
                startedAt,
                CompressionErrorCategory.EngineUnavailable,
                $"Engine '{engineId}' is not configured.");
        }

        EngineCapability capability;
        try
        {
            capability = await engine
                .DetectCapabilityAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Canceled(job, startedAt, null);
        }
        catch (Exception exception)
        {
            return Failure(
                job,
                startedAt,
                CompressionErrorCategory.EngineUnavailable,
                exception.Message);
        }

        if (!capability.IsAvailable)
        {
            return Failure(
                job,
                startedAt,
                CompressionErrorCategory.EngineUnavailable,
                capability.UnavailableReason ?? "Engine is unavailable.");
        }

        TemporaryOutputFile temporaryOutput;
        try
        {
            temporaryOutput = outputPublisher.CreateTemporaryFile(job);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                job,
                startedAt,
                CompressionErrorCategory.FileSystemError,
                "Temporary output could not be created.");
        }

        var runtimeLimit = runtimeLimits.For(engineId);
        using var timeoutSource = runtimeLimit is TimeSpan limit
            ? new CancellationTokenSource(limit, clock)
            : null;
        using var linkedSource = timeoutSource is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutSource.Token);
        var encodeToken = linkedSource?.Token ?? cancellationToken;

        EngineEncodingResult encodingResult;
        try
        {
            encodingResult = await engine
                .EncodeAsync(job, temporaryOutput.Path, encodeToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (encodeToken.IsCancellationRequested)
        {
            return CanceledOrTimedOut(
                job, startedAt, capability, temporaryOutput,
                timeoutSource, cancellationToken, runtimeLimit);
        }
        catch (Exception exception)
        {
            return Cleanup(
                temporaryOutput,
                () => Failure(
                    job,
                    startedAt,
                    CompressionErrorCategory.Unexpected,
                    exception.Message,
                    capability.BuildVersion));
        }

        if (encodingResult.Status is EngineEncodingStatus.Canceled)
        {
            return CanceledOrTimedOut(
                job, startedAt, capability, temporaryOutput,
                timeoutSource, cancellationToken, runtimeLimit);
        }

        if (encodingResult.Status is EngineEncodingStatus.Failed)
        {
            return Cleanup(
                temporaryOutput,
                () => Failure(
                    job,
                    startedAt,
                    encodingResult.ErrorCategory ?? CompressionErrorCategory.EngineFailed,
                    encodingResult.ErrorText ?? "Encoding failed.",
                    capability.BuildVersion));
        }

        try
        {
            var publication = outputPublisher.Publish(job, temporaryOutput);
            var published = publication.Disposition is OutputPublicationDisposition.Published;
            var warning = publication.Disposition switch
            {
                OutputPublicationDisposition.DiscardedNotSmaller =>
                    "Encoded output was not smaller and was discarded.",
                OutputPublicationDisposition.DiscardedBelowMinimumSavings =>
                    $"Encoded output saved less than the required {job.MinimumSavingsPercent}% and was discarded.",
                _ => null
            };
            return new(
                job.Id,
                JobStatus.Succeeded,
                job.InputPath,
                job.OutputPath,
                engine.EngineId,
                capability.BuildVersion,
                job.InputImageInfo.FileSizeBytes,
                publication.EncodedSizeBytes,
                startedAt,
                clock.GetUtcNow(),
                true,
                published,
                warning,
                null,
                null);
        }
        catch (OutputPublicationException exception)
        {
            return Failure(
                job,
                startedAt,
                exception.Category,
                exception.Message,
                capability.BuildVersion);
        }
    }

    /// <summary>
    /// Ein durch das Zeitlimit ausgelöster Abbruch ist eine Laufzeitüberschreitung
    /// (<see cref="CompressionErrorCategory.LimitExceeded"/>), kein Benutzerabbruch. Ein
    /// gleichzeitiger Benutzerabbruch hat Vorrang und bleibt <see cref="JobStatus.Canceled"/>
    /// (MP-004, Abschnitt 7.1). In beiden Fällen wird die temporäre Datei entfernt.
    /// </summary>
    private CompressionExecutionResult CanceledOrTimedOut(
        CompressionJob job,
        DateTimeOffset startedAt,
        EngineCapability capability,
        TemporaryOutputFile temporaryOutput,
        CancellationTokenSource? timeoutSource,
        CancellationToken userToken,
        TimeSpan? runtimeLimit)
    {
        if (timeoutSource is { IsCancellationRequested: true } && !userToken.IsCancellationRequested)
        {
            var seconds = runtimeLimit!.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            return Cleanup(
                temporaryOutput,
                () => Failure(
                    job,
                    startedAt,
                    CompressionErrorCategory.LimitExceeded,
                    $"Encoding exceeded the runtime limit of {seconds} seconds.",
                    capability.BuildVersion));
        }

        return Cleanup(
            temporaryOutput,
            () => Canceled(job, startedAt, capability.BuildVersion));
    }

    private CompressionExecutionResult Cleanup(
        TemporaryOutputFile temporaryOutput,
        Func<CompressionExecutionResult> resultFactory)
    {
        try
        {
            outputPublisher.Discard(temporaryOutput);
            return resultFactory();
        }
        catch (OutputPublicationException exception)
        {
            var originalResult = resultFactory();
            return Failure(
                originalResult.JobId,
                originalResult.InputPath,
                originalResult.OutputPath,
                originalResult.EngineId,
                originalResult.InputSizeBytes,
                originalResult.StartedAt,
                CompressionErrorCategory.FileSystemError,
                exception.Message,
                originalResult.EngineVersion);
        }
    }

    /// <summary>
    /// Erfolgreicher Job für eine bereits optimierte Eingabe: kein Encoding, keine veröffentlichte
    /// Ausgabe, ein Hinweis (Issue #1). Wie die <c>discard</c>-Richtlinie ist dies ein Erfolg ohne
    /// Ausgabedatei (Abschnitt 17.2).
    /// </summary>
    private CompressionExecutionResult Skipped(CompressionJob job, DateTimeOffset startedAt) =>
        new(
            job.Id,
            JobStatus.Succeeded,
            job.InputPath,
            job.OutputPath,
            job.EngineSettings.EngineId,
            null,
            job.InputImageInfo.FileSizeBytes,
            null,
            startedAt,
            clock.GetUtcNow(),
            false,
            false,
            "Input was already optimized by PicCompressor; skipped.",
            null,
            null);

    private CompressionExecutionResult Failure(
        CompressionJob job,
        DateTimeOffset startedAt,
        CompressionErrorCategory category,
        string errorText,
        string? engineVersion = null) =>
        Failure(
            job.Id,
            job.InputPath,
            job.OutputPath,
            job.EngineSettings.EngineId,
            job.InputImageInfo.FileSizeBytes,
            startedAt,
            category,
            errorText,
            engineVersion);

    private CompressionExecutionResult Failure(
        Guid jobId,
        string inputPath,
        string outputPath,
        string engineId,
        long inputSizeBytes,
        DateTimeOffset startedAt,
        CompressionErrorCategory category,
        string errorText,
        string? engineVersion = null) =>
        new(
            jobId,
            JobStatus.Failed,
            inputPath,
            outputPath,
            engineId,
            engineVersion,
            inputSizeBytes,
            null,
            startedAt,
            clock.GetUtcNow(),
            false,
            false,
            null,
            category,
            errorText);

    private CompressionExecutionResult Canceled(
        CompressionJob job,
        DateTimeOffset startedAt,
        string? engineVersion) =>
        new(
            job.Id,
            JobStatus.Canceled,
            job.InputPath,
            job.OutputPath,
            job.EngineSettings.EngineId,
            engineVersion,
            job.InputImageInfo.FileSizeBytes,
            null,
            startedAt,
            clock.GetUtcNow(),
            false,
            false,
            null,
            CompressionErrorCategory.Canceled,
            "Encoding was canceled.");
}
