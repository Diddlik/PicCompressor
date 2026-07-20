using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed record CompressionHistoryEntry(
    DateTimeOffset CompletedAt,
    string FileName,
    string EngineId,
    long InputSizeBytes,
    long? OutputSizeBytes,
    JobStatus Status,
    CompressionErrorCategory? ErrorCategory);

public interface ICompressionHistoryStore
{
    Task<IReadOnlyList<CompressionHistoryEntry>> GetAsync(CancellationToken cancellationToken);

    Task AppendAsync(
        CompressionHistoryEntry entry,
        CancellationToken cancellationToken);
}
