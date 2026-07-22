namespace PicCompressor.Infrastructure.Tests;

public sealed class PhysicalInputDiscoveryTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-discovery-{Guid.NewGuid():N}");

    public PhysicalInputDiscoveryTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "a.PNG"), "");
        File.WriteAllText(Path.Combine(directory, "ignored.txt"), "");
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        File.WriteAllText(Path.Combine(directory, "nested", "b.jpg"), "");
    }

    [Fact]
    public void Discover_returns_supported_top_level_images()
    {
        var discovery = new PhysicalInputDiscovery(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var inputs = discovery.Discover([directory], recursive: false, excludedDirectory: null);

        var input = Assert.Single(inputs);
        Assert.Equal(Path.Combine(directory, "a.PNG"), input.Path);
        Assert.Equal("", input.RelativeDirectory);
    }

    [Fact]
    public void Discover_recursive_preserves_relative_directory_and_excludes_output_tree()
    {
        var output = Path.Combine(directory, "output");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "old.jpg"), "");
        var discovery = new PhysicalInputDiscovery(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var inputs = discovery.Discover([directory], recursive: true, excludedDirectory: output);

        Assert.Collection(
            inputs.OrderBy(input => input.Path),
            input => Assert.Equal("", input.RelativeDirectory),
            input => Assert.Equal("nested", input.RelativeDirectory));
        Assert.DoesNotContain(inputs, input => input.Path.Contains("old.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverAsync_matches_the_synchronous_result_and_reports_size_and_progress()
    {
        File.WriteAllText(Path.Combine(directory, "sized.jpg"), "0123456789");
        var discovery = Comparer();
        var counts = new List<int>();
        var progress = new Progress<int>(counts.Add);

        var inputs = await discovery.DiscoverAsync(
            [directory], recursive: true, excludedDirectory: null, progress, CancellationToken.None);

        var sync = discovery.Discover([directory], recursive: true, excludedDirectory: null);
        Assert.Equal(sync.Select(input => input.Path), inputs.Select(input => input.Path));
        Assert.Contains(inputs, input => input.Path.EndsWith("sized.jpg", StringComparison.Ordinal)
            && input.FileSizeBytes == 10);
    }

    [Fact]
    public async Task DiscoverAsync_throws_when_cancelled_before_it_runs()
    {
        var discovery = Comparer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.DiscoverAsync(
                [directory], recursive: true, excludedDirectory: null, progress: null, cancellation.Token));
    }

    private static PhysicalInputDiscovery Comparer() =>
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
