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

        var stored = await new SqliteCompressionHistoryStore(databasePath)
            .AppendAsync(record, CancellationToken.None);
        var records = await new SqliteCompressionHistoryStore(databasePath)
            .GetAsync(CancellationToken.None);

        // Der Speicher vergibt die Kennung; auf einer frischen Datenbank ist das die 1.
        Assert.Equal(1, stored.Id);
        Assert.Equal(record with { Id = 1 }, Assert.Single(records));
    }

    [Fact]
    public async Task DeleteAsync_removes_only_the_named_entry()
    {
        var store = CreateStore();
        var kept = await store.AppendAsync(Entry("keep.png", Days(-2)), CancellationToken.None);
        var removed = await store.AppendAsync(Entry("drop.png", Days(-1)), CancellationToken.None);

        await store.DeleteAsync(removed.Id, CancellationToken.None);

        var remaining = Assert.Single(await store.GetAsync(CancellationToken.None));
        Assert.Equal(kept.Id, remaining.Id);
        Assert.Equal("keep.png", remaining.FileName);
    }

    [Fact]
    public async Task ClearAsync_removes_every_entry()
    {
        var store = CreateStore();
        await store.AppendAsync(Entry("a.png", Days(-1)), CancellationToken.None);
        await store.AppendAsync(Entry("b.png", Days(-2)), CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Empty(await store.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplyRetentionAsync_removes_only_entries_before_the_cutoff()
    {
        var store = CreateStore();
        await store.AppendAsync(Entry("old.png", Days(-40)), CancellationToken.None);
        await store.AppendAsync(Entry("recent.png", Days(-5)), CancellationToken.None);

        var removed = await store.ApplyRetentionAsync(Days(-30), CancellationToken.None);

        Assert.Equal(1, removed);
        var remaining = Assert.Single(await store.GetAsync(CancellationToken.None));
        Assert.Equal("recent.png", remaining.FileName);
    }

    /// <summary>
    /// Eine Datenbank aus einer älteren Schemaversion muss vorwärts migriert werden,
    /// ohne bestehende Einträge zu verlieren (Abschnitt 13.1).
    /// </summary>
    [Fact]
    public async Task Opening_a_version_1_database_migrates_it_without_losing_entries()
    {
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "history.db");
        CreateVersion1Database(databasePath);

        var records = await new SqliteCompressionHistoryStore(databasePath)
            .GetAsync(CancellationToken.None);

        Assert.Equal("legacy.png", Assert.Single(records).FileName);
        Assert.Equal(2, ReadUserVersion(databasePath));
        Assert.True(HasCompletedAtIndex(databasePath));
    }

    [Fact]
    public async Task Opening_a_newer_schema_is_refused()
    {
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "history.db");
        CreateVersion1Database(databasePath);
        ExecuteSql(databasePath, "PRAGMA user_version = 99;");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SqliteCompressionHistoryStore(databasePath)
                .GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_appends_are_all_persisted()
    {
        var store = CreateStore();

        await Task.WhenAll(
            Enumerable.Range(0, 32).Select(index =>
                store.AppendAsync(Entry($"file{index}.png", Days(-1)), CancellationToken.None)));

        var records = await store.GetAsync(CancellationToken.None);
        Assert.Equal(32, records.Count);
        Assert.Equal(32, records.Select(record => record.FileName).Distinct().Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private SqliteCompressionHistoryStore CreateStore()
    {
        Directory.CreateDirectory(directory);
        return new SqliteCompressionHistoryStore(Path.Combine(directory, "history.db"));
    }

    private static DateTimeOffset Days(int offset) =>
        DateTimeOffset.Parse("2026-07-20T12:00:00Z").AddDays(offset);

    private static CompressionHistoryEntry Entry(string fileName, DateTimeOffset completedAt) =>
        new(
            completedAt,
            fileName,
            JpegliSettings.JpegliEngineId,
            100,
            50,
            JobStatus.Succeeded,
            null);

    /// <summary>Erzeugt eine Datenbank im Schema vor der Index-Migration.</summary>
    private static void CreateVersion1Database(string databasePath) =>
        ExecuteSql(
            databasePath,
            """
            CREATE TABLE compression_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                completed_at TEXT NOT NULL,
                file_name TEXT NOT NULL,
                engine_id TEXT NOT NULL,
                input_size_bytes INTEGER NOT NULL CHECK (input_size_bytes >= 0),
                output_size_bytes INTEGER NULL CHECK (output_size_bytes >= 0),
                status INTEGER NOT NULL,
                error_category INTEGER NULL
            );
            INSERT INTO compression_history (
                completed_at, file_name, engine_id, input_size_bytes,
                output_size_bytes, status, error_category)
            VALUES ('2026-07-19T12:00:00.0000000+00:00', 'legacy.png', 'jpegli', 100, 50, 5, NULL);
            PRAGMA user_version = 1;
            """);

    private static void ExecuteSql(string databasePath, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ReadUserVersion(string databasePath) =>
        Convert.ToInt32(Scalar(databasePath, "PRAGMA user_version;"));

    private static bool HasCompletedAtIndex(string databasePath) =>
        Convert.ToInt32(
            Scalar(
                databasePath,
                """
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'index' AND name = 'ix_compression_history_completed_at';
                """)) == 1;

    private static object? Scalar(string databasePath, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
