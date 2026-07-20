using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed class EngineCapability
{
    private EngineCapability(
        string engineId,
        bool isAvailable,
        string? buildVersion,
        string? sourceRevision,
        string? unavailableReason)
    {
        EngineId = engineId;
        IsAvailable = isAvailable;
        BuildVersion = buildVersion;
        SourceRevision = sourceRevision;
        UnavailableReason = unavailableReason;
    }

    public string EngineId { get; }
    public bool IsAvailable { get; }
    public string? BuildVersion { get; }
    public string? SourceRevision { get; }
    public string? UnavailableReason { get; }

    public static EngineCapability Available(
        string engineId,
        string buildVersion,
        string sourceRevision) =>
        new(engineId, true, buildVersion, sourceRevision, null);

    public static EngineCapability Unavailable(string engineId, string reason) =>
        new(engineId, false, null, null, reason);
}

public enum EngineEncodingStatus
{
    Succeeded,
    Failed,
    Canceled
}

public sealed class EngineEncodingResult
{
    private EngineEncodingResult(
        EngineEncodingStatus status,
        CompressionErrorCategory? errorCategory,
        string? errorText,
        TimeSpan duration)
    {
        Status = status;
        ErrorCategory = errorCategory;
        ErrorText = errorText;
        Duration = duration;
    }

    public EngineEncodingStatus Status { get; }
    public CompressionErrorCategory? ErrorCategory { get; }
    public string? ErrorText { get; }
    public TimeSpan Duration { get; }

    public static EngineEncodingResult Succeeded(TimeSpan duration) =>
        new(EngineEncodingStatus.Succeeded, null, null, duration);

    public static EngineEncodingResult Failed(
        CompressionErrorCategory category,
        string errorText,
        TimeSpan duration) =>
        new(EngineEncodingStatus.Failed, category, errorText, duration);

    public static EngineEncodingResult Canceled(TimeSpan duration) =>
        new(
            EngineEncodingStatus.Canceled,
            CompressionErrorCategory.Canceled,
            "Encoding was canceled.",
            duration);
}

public interface ICompressionEngine
{
    string EngineId { get; }

    Task<EngineCapability> DetectCapabilityAsync(CancellationToken cancellationToken);

    Task<EngineEncodingResult> EncodeAsync(
        CompressionJob job,
        string temporaryOutputPath,
        CancellationToken cancellationToken);
}
