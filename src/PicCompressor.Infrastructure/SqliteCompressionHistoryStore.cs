using Microsoft.Data.Sqlite;
using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Infrastructure;

public sealed class SqliteCompressionHistoryStore : ICompressionHistoryStore
{
    /// <summary>
    /// Vorwärtsgerichtete Migrationen (Abschnitt 13.1). Eintrag <c>i</c> hebt das
    /// Schema von Version <c>i</c> auf <c>i + 1</c>. Bestehende Einträge dürfen
    /// nie verworfen und Schritte nie umgeschrieben werden; eine Änderung wird
    /// als weiterer Schritt angehängt.
    /// </summary>
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE IF NOT EXISTS compression_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            completed_at TEXT NOT NULL,
            file_name TEXT NOT NULL,
            engine_id TEXT NOT NULL,
            input_size_bytes INTEGER NOT NULL CHECK (input_size_bytes >= 0),
            output_size_bytes INTEGER NULL CHECK (output_size_bytes >= 0),
            status INTEGER NOT NULL,
            error_category INTEGER NULL
        );
        """,
        // Abfrage und Aufbewahrung laufen beide über completed_at.
        """
        CREATE INDEX IF NOT EXISTS ix_compression_history_completed_at
            ON compression_history (completed_at);
        """
    ];

    private static readonly int SchemaVersion = Migrations.Length;

    private readonly string databasePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public SqliteCompressionHistoryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = Path.GetFullPath(databasePath);
    }

    public async Task<IReadOnlyList<CompressionHistoryEntry>> GetAsync(
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized(cancellationToken);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT completed_at, file_name, engine_id, input_size_bytes,
                       output_size_bytes, status, error_category
                FROM compression_history
                ORDER BY completed_at DESC, id DESC;
                """;
            using var reader = command.ExecuteReader();
            var entries = new List<CompressionHistoryEntry>();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(
                    new(
                        DateTimeOffset.Parse(
                            reader.GetString(0),
                            System.Globalization.CultureInfo.InvariantCulture),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt64(3),
                        reader.IsDBNull(4) ? null : reader.GetInt64(4),
                        (JobStatus)reader.GetInt32(5),
                        reader.IsDBNull(6)
                            ? null
                            : (CompressionErrorCategory)reader.GetInt32(6)));
            }

            return entries;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AppendAsync(
        CompressionHistoryEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.EngineId);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.InputSizeBytes);
        if (entry.OutputSizeBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entry));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized(cancellationToken);
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO compression_history (
                    completed_at, file_name, engine_id, input_size_bytes,
                    output_size_bytes, status, error_category)
                VALUES (
                    $completed_at, $file_name, $engine_id, $input_size_bytes,
                    $output_size_bytes, $status, $error_category);
                """;
            command.Parameters.AddWithValue(
                "$completed_at",
                entry.CompletedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$file_name", entry.FileName);
            command.Parameters.AddWithValue("$engine_id", entry.EngineId);
            command.Parameters.AddWithValue("$input_size_bytes", entry.InputSizeBytes);
            command.Parameters.AddWithValue(
                "$output_size_bytes",
                entry.OutputSizeBytes is long outputSize ? outputSize : DBNull.Value);
            command.Parameters.AddWithValue("$status", (int)entry.Status);
            command.Parameters.AddWithValue(
                "$error_category",
                entry.ErrorCategory is CompressionErrorCategory category
                    ? (int)category
                    : DBNull.Value);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized(cancellationToken);
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM compression_history;";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> ApplyRetentionAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized(cancellationToken);
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // completed_at wird als ISO-8601 in UTC abgelegt, deshalb ist der
            // lexikografische Vergleich zugleich der chronologische.
            command.CommandText =
                "DELETE FROM compression_history WHERE completed_at < $cutoff;";
            command.Parameters.AddWithValue(
                "$cutoff",
                cutoff.ToUniversalTime().ToString("O"));
            var removed = command.ExecuteNonQuery();
            transaction.Commit();
            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    private void EnsureInitialized(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var connection = OpenConnection();
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version > SchemaVersion)
        {
            throw new InvalidDataException(
                $"History schema version {version} is newer than supported version {SchemaVersion}.");
        }

        if (version < SchemaVersion)
        {
            using var transaction = connection.BeginTransaction();
            for (var step = version; step < SchemaVersion; step++)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = Migrations[step];
                command.ExecuteNonQuery();
            }

            // PRAGMA akzeptiert keine Parameter; der Wert stammt aus der
            // Migrationsliste und nicht aus einer Eingabe.
            using var versionUpdate = connection.CreateCommand();
            versionUpdate.Transaction = transaction;
            versionUpdate.CommandText = $"PRAGMA user_version = {SchemaVersion};";
            versionUpdate.ExecuteNonQuery();
            transaction.Commit();
        }

        initialized = true;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 5,
                Pooling = false
            }.ToString());
        connection.Open();
        return connection;
    }
}
