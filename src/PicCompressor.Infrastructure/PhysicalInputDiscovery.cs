using PicCompressor.Application;

namespace PicCompressor.Infrastructure;

public sealed class PhysicalInputDiscovery(StringComparer pathComparer) : IInputDiscovery
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public IReadOnlyList<DiscoveredInput> Discover(
        IEnumerable<string> inputPaths,
        bool recursive,
        string? excludedDirectory)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        return DiscoverCore(
            inputPaths.ToList(), recursive, excludedDirectory, progress: null, CancellationToken.None);
    }

    public Task<IReadOnlyList<DiscoveredInput>> DiscoverAsync(
        IReadOnlyList<string> inputPaths,
        bool recursive,
        string? excludedDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        // Die Enumeration ist blockierendes Datei-I/O; sie läuft abseits des UI-Threads
        // (Abschnitt 17.3), bleibt aber kooperativ abbrechbar.
        return Task.Run(
            () => DiscoverCore(inputPaths, recursive, excludedDirectory, progress, cancellationToken),
            cancellationToken);
    }

    private IReadOnlyList<DiscoveredInput> DiscoverCore(
        IReadOnlyList<string> inputPaths,
        bool recursive,
        string? excludedDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<DiscoveredInput>();
        var seen = new HashSet<string>(pathComparer);
        var excludedPath = excludedDirectory is null
            ? null
            : Path.GetFullPath(excludedDirectory);

        foreach (var inputPath in inputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canonicalPath = Path.GetFullPath(inputPath);
            if (File.Exists(canonicalPath))
            {
                Add(canonicalPath, "", results, seen, progress);
                continue;
            }

            if (!Directory.Exists(canonicalPath))
            {
                throw new FileNotFoundException("Input path does not exist.", canonicalPath);
            }

            var pending = new Stack<string>();
            pending.Push(canonicalPath);
            while (pending.TryPop(out var currentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsExcluded(currentDirectory, excludedPath))
                {
                    continue;
                }

                var relativeDirectory = Path.GetRelativePath(canonicalPath, currentDirectory);
                if (relativeDirectory == ".")
                {
                    relativeDirectory = "";
                }

                foreach (var file in Directory.EnumerateFiles(currentDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Add(file, relativeDirectory, results, seen, progress);
                }

                if (!recursive)
                {
                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(currentDirectory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        return results.OrderBy(input => input.Path, pathComparer).ToArray();
    }

    private static bool IsExcluded(string directory, string? excludedDirectory)
    {
        if (excludedDirectory is null)
        {
            return false;
        }

        var relative = Path.GetRelativePath(excludedDirectory, directory);
        return relative == "."
            || (!Path.IsPathFullyQualified(relative)
                && relative != ".."
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static void Add(
        string path,
        string relativeDirectory,
        ICollection<DiscoveredInput> results,
        ISet<string> seen,
        IProgress<int>? progress)
    {
        var canonicalPath = Path.GetFullPath(path);
        if (!SupportedExtensions.Contains(Path.GetExtension(canonicalPath)) || !seen.Add(canonicalPath))
        {
            return;
        }

        long size;
        try
        {
            size = new FileInfo(canonicalPath).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Die belastbare Prüfung folgt beim Ausführen; die Größe ist hier nur für die Anzeige.
            size = 0;
        }

        results.Add(new(canonicalPath, relativeDirectory, size));
        progress?.Report(results.Count);
    }
}
