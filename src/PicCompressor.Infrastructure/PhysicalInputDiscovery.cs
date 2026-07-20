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
        var results = new List<DiscoveredInput>();
        var seen = new HashSet<string>(pathComparer);
        var excludedPath = excludedDirectory is null
            ? null
            : Path.GetFullPath(excludedDirectory);

        foreach (var inputPath in inputPaths)
        {
            var canonicalPath = Path.GetFullPath(inputPath);
            if (File.Exists(canonicalPath))
            {
                Add(canonicalPath, "", results, seen);
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
                    Add(file, relativeDirectory, results, seen);
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
        ISet<string> seen)
    {
        var canonicalPath = Path.GetFullPath(path);
        if (SupportedExtensions.Contains(Path.GetExtension(canonicalPath))
            && seen.Add(canonicalPath))
        {
            results.Add(new(canonicalPath, relativeDirectory));
        }
    }
}
