using PicCompressor.Application;
using PicCompressor.Gui.Services;

namespace PicCompressor.Desktop;

public sealed class PersistentHistoryService(ICompressionHistoryStore store) : IHistoryService
{
    public bool IsAvailable => true;

    public async Task<IReadOnlyList<HistoryRecord>> GetAsync(
        CancellationToken cancellationToken) =>
        (await store.GetAsync(cancellationToken).ConfigureAwait(false))
        .Select(Map)
        .ToArray();

    public async Task<HistoryRecord> AppendAsync(
        HistoryRecord record,
        CancellationToken cancellationToken) =>
        Map(await store.AppendAsync(
            new(
                record.CompletedAt,
                record.FileName,
                record.EngineId,
                record.InputSizeBytes,
                record.OutputSizeBytes,
                record.Status,
                record.ErrorCategory),
            cancellationToken).ConfigureAwait(false));

    public Task DeleteAsync(long id, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken) =>
        store.ClearAsync(cancellationToken);

    private static HistoryRecord Map(CompressionHistoryEntry entry) =>
        new(
            entry.CompletedAt,
            entry.FileName,
            entry.EngineId,
            entry.InputSizeBytes,
            entry.OutputSizeBytes,
            entry.Status,
            entry.ErrorCategory)
        {
            Id = entry.Id
        };
}
