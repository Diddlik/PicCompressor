using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed record CompressionHistoryEntry(
    DateTimeOffset CompletedAt,
    string FileName,
    string EngineId,
    long InputSizeBytes,
    long? OutputSizeBytes,
    JobStatus Status,
    CompressionErrorCategory? ErrorCategory)
{
    /// <summary>
    /// Stabile Kennung des persistierten Eintrags. Beim Anhängen unerheblich (0); der
    /// Speicher vergibt sie und liefert sie beim Lesen und als Rückgabe von
    /// <see cref="ICompressionHistoryStore.AppendAsync"/> zurück, damit ein einzelner
    /// Eintrag gezielt gelöscht werden kann (Abschnitt 13.1).
    /// </summary>
    public long Id { get; init; }
}

public interface ICompressionHistoryStore
{
    Task<IReadOnlyList<CompressionHistoryEntry>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Speichert den Eintrag und liefert ihn mit der vergebenen <see cref="CompressionHistoryEntry.Id"/> zurück.</summary>
    Task<CompressionHistoryEntry> AppendAsync(
        CompressionHistoryEntry entry,
        CancellationToken cancellationToken);

    /// <summary>Entfernt den Eintrag mit der angegebenen Kennung (Abschnitt 13.1).</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken);

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
