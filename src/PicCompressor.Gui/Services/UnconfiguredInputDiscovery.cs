using PicCompressor.Application;

namespace PicCompressor.Gui.Services;

/// <summary>
/// Standard-Discovery ohne verdrahteten Adapter: findet nichts. Der Desktop Host verdrahtet die
/// echte, filesystembasierte Implementierung; die GUI kennt nur den Application-Port (Abschnitt 14.1).
/// </summary>
public sealed class UnconfiguredInputDiscovery : IInputDiscovery
{
    public IReadOnlyList<DiscoveredInput> Discover(
        IEnumerable<string> inputPaths,
        bool recursive,
        string? excludedDirectory) => [];

    public Task<IReadOnlyList<DiscoveredInput>> DiscoverAsync(
        IReadOnlyList<string> inputPaths,
        bool recursive,
        string? excludedDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DiscoveredInput>>([]);
}
