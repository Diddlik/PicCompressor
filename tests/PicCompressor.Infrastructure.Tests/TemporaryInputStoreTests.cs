using PicCompressor.Infrastructure;

namespace PicCompressor.Infrastructure.Tests;

/// <summary>
/// Verwaltete temporäre Eingaben (MP-003): vollständig geschriebene Dateien, eindeutige Namen und
/// keine Reste früherer Läufe.
/// </summary>
public sealed class TemporaryInputStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "piccompressor-temporary-inputs",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Aufräumfehler dürfen den Test nicht kippen.
        }
    }

    [Fact]
    public async Task Save_writes_the_full_content_and_leaves_no_reservation()
    {
        var store = new TemporaryInputStore(directory);
        byte[] content = [1, 2, 3, 4];

        var path = await store.SaveAsync(content, ".png", CancellationToken.None);

        Assert.Equal(".png", Path.GetExtension(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory, "*.partial"));
    }

    [Fact]
    public async Task Save_never_reuses_a_name()
    {
        var store = new TemporaryInputStore(directory);

        var first = await store.SaveAsync(new byte[] { 1 }, ".png", CancellationToken.None);
        var second = await store.SaveAsync(new byte[] { 2 }, ".png", CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public async Task Leftovers_of_earlier_runs_are_removed()
    {
        var earlier = new TemporaryInputStore(directory);
        await earlier.SaveAsync(new byte[] { 1 }, ".png", CancellationToken.None);

        new TemporaryInputStore(directory).ClearPreviousRuns();

        Assert.False(Directory.Exists(directory));
    }
}
