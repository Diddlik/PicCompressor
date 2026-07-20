namespace PicCompressor.Infrastructure.Tests;

public sealed class SqliteCompressionHistoryStoreTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-history-{Guid.NewGuid():N}");

    [Fact]
    public async Task AppendAsync_persists_records_across_store_instances()
    {
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "history.db");
        var record = new CompressionHistoryEntry(
            DateTimeOffset.Parse("2026-07-19T12:00:00Z"),
            "image.png",
            JpegliSettings.JpegliEngineId,
            100,
            50,
            JobStatus.Succeeded,
            null);

        await new SqliteCompressionHistoryStore(databasePath)
            .AppendAsync(record, CancellationToken.None);
        var records = await new SqliteCompressionHistoryStore(databasePath)
            .GetAsync(CancellationToken.None);

        Assert.Equal(record, Assert.Single(records));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
