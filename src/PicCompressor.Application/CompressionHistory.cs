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

    /// <summary>Entfernt alle Einträge (Abschnitt 13.1).</summary>
    Task ClearAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Entfernt Einträge, die vor <paramref name="cutoff"/> abgeschlossen wurden,
    /// und meldet deren Anzahl. Setzt die Aufbewahrungsdauer aus Abschnitt 13.1 um.
    /// </summary>
    Task<int> ApplyRetentionAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);
}
