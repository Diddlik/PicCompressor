namespace PicCompressor.Application;

public sealed record DiscoveredInput(string Path, string RelativeDirectory);

public interface IInputDiscovery
{
    IReadOnlyList<DiscoveredInput> Discover(
        IEnumerable<string> inputPaths,
        bool recursive,
        string? excludedDirectory);
}
