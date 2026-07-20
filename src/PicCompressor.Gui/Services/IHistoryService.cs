using PicCompressor.Domain;

namespace PicCompressor.Gui.Services;

/// <summary>Ein persistierter Verlaufseintrag (Abschnitt 13.1). Bildinhalte werden nie gespeichert.</summary>
public sealed record HistoryRecord(
    DateTimeOffset CompletedAt,
    string FileName,
    string EngineId,
    long InputSizeBytes,
    long? OutputSizeBytes,
    JobStatus Status,
    CompressionErrorCategory? ErrorCategory);

public interface IHistoryService
{
    /// <summary>
    /// Meldet, ob ein Verlaufsspeicher verdrahtet ist. Ohne Speicher zeigt die Oberfläche das
    /// offen an, statt einen leeren Verlauf als vollständig auszugeben.
    /// </summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<HistoryRecord>> GetAsync(CancellationToken cancellationToken);

    Task AppendAsync(HistoryRecord record, CancellationToken cancellationToken);
}

/// <summary>
/// Standardimplementierung ohne Persistenz: hält den Verlauf der laufenden Sitzung und weist
/// über <see cref="IsAvailable"/> aus, dass nichts gespeichert wird.
/// </summary>
public sealed class InMemoryHistoryService : IHistoryService
{
    private readonly List<HistoryRecord> records = [];

    public bool IsAvailable => false;

    public Task<IReadOnlyList<HistoryRecord>> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (records)
        {
            return Task.FromResult<IReadOnlyList<HistoryRecord>>([.. records]);
        }
    }

    public Task AppendAsync(HistoryRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        lock (records)
        {
            records.Insert(0, record);
        }

        return Task.CompletedTask;
    }
}
