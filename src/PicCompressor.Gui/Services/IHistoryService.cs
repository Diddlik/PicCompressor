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
    CompressionErrorCategory? ErrorCategory)
{
    /// <summary>Stabile Kennung des Eintrags; vom Speicher vergeben, beim Anhängen unerheblich (0).</summary>
    public long Id { get; init; }
}

public interface IHistoryService
{
    /// <summary>
    /// Meldet, ob ein Verlaufsspeicher verdrahtet ist. Ohne Speicher zeigt die Oberfläche das
    /// offen an, statt einen leeren Verlauf als vollständig auszugeben.
    /// </summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<HistoryRecord>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Speichert den Eintrag und liefert ihn mit der vergebenen <see cref="HistoryRecord.Id"/> zurück.</summary>
    Task<HistoryRecord> AppendAsync(HistoryRecord record, CancellationToken cancellationToken);

    /// <summary>Löscht den Eintrag mit der angegebenen Kennung (Abschnitt 13.1).</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken);

    /// <summary>Löscht den gesamten Verlauf (Abschnitt 13.1).</summary>
    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Standardimplementierung ohne Persistenz: hält den Verlauf der laufenden Sitzung und weist
/// über <see cref="IsAvailable"/> aus, dass nichts gespeichert wird.
/// </summary>
public sealed class InMemoryHistoryService : IHistoryService
{
    private readonly List<HistoryRecord> records = [];
    private long nextId;

    public bool IsAvailable => false;

    public Task<IReadOnlyList<HistoryRecord>> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (records)
        {
            return Task.FromResult<IReadOnlyList<HistoryRecord>>([.. records]);
        }
    }

    public Task<HistoryRecord> AppendAsync(HistoryRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        lock (records)
        {
            var stored = record with { Id = ++nextId };
            records.Insert(0, stored);
            return Task.FromResult(stored);
        }
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (records)
        {
            records.RemoveAll(record => record.Id == id);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (records)
        {
            records.Clear();
        }

        return Task.CompletedTask;
    }
}
