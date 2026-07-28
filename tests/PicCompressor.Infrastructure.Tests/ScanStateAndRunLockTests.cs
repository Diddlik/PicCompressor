using PicCompressor.Application;

namespace PicCompressor.Infrastructure.Tests;

public sealed class ScanStateAndRunLockTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-scan-{Guid.NewGuid():N}");

    public ScanStateAndRunLockTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Load_returns_an_empty_state_for_a_missing_file()
    {
        var store = new JsonScanStateStore(Path.Combine(directory, "missing", "state.json"));

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_and_load_round_trip_every_field()
    {
        var path = Path.Combine(directory, "nested", "state.json");
        var store = new JsonScanStateStore(path);
        var entry = new ScanStateEntry("/photos/a.png", 1234, 638_000_000_000_000_000, "abc", "/out/a.jpg");

        store.Save([entry]);

        Assert.Equal(entry, Assert.Single(new JsonScanStateStore(path).Load()));
        Assert.False(File.Exists($"{path}.tmp"));
    }

    [Fact]
    public void Load_rejects_a_newer_schema_version()
    {
        var path = Path.Combine(directory, "state.json");
        File.WriteAllText(path, """{ "SchemaVersion": 99, "Entries": [] }""");

        Assert.Throws<InvalidDataException>(() => new JsonScanStateStore(path).Load());
    }

    [Fact]
    public void TryAcquire_reports_a_lock_that_another_run_holds()
    {
        var path = Path.Combine(directory, "run.lock");

        using (var first = RunLock.TryAcquire(path))
        {
            Assert.NotNull(first);
            Assert.Null(RunLock.TryAcquire(path));
        }

        // Die Sperre wird beim Beenden freigegeben; ein späterer Lauf ist nicht blockiert.
        using var later = RunLock.TryAcquire(path);
        Assert.NotNull(later);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
