using System.Security.Cryptography;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Infrastructure;

namespace PicCompressor.Web;

public sealed record ConfigurationDocument(ScanConfiguration Configuration, string Revision);

public sealed class ConfigurationValidationException(IReadOnlyList<string> errors)
    : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed class ConfigurationConflictException()
    : Exception("The configuration changed since it was loaded. Reload it and try again.");

public sealed class WebConfigurationService
{
    private const string MissingRevision = "missing";
    private readonly string configurationPath;
    private readonly string dataRoot;
    private readonly string stateRoot;
    private readonly StringComparer pathComparer;
    private readonly StringComparison pathComparison;
    private readonly Lock gate = new();

    public WebConfigurationService(string configurationPath, string dataRoot, string stateRoot)
    {
        this.configurationPath = Path.GetFullPath(
            !string.IsNullOrWhiteSpace(configurationPath)
                ? configurationPath
                : throw new ArgumentException("Configuration path is required.", nameof(configurationPath)));
        this.dataRoot = NormalizeRoot(dataRoot, nameof(dataRoot));
        this.stateRoot = NormalizeRoot(stateRoot, nameof(stateRoot));
        pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public ConfigurationDocument Load()
    {
        lock (gate)
        {
            return File.Exists(configurationPath)
                ? new(new JsonScanConfigurationStore(configurationPath).Load(), RevisionOf(configurationPath))
                : CreateDefault();
        }
    }

    public ConfigurationDocument CreateDefault() =>
        new(
            new(
                SchemaVersion: ScanConfiguration.CurrentSchemaVersion,
                Mode: ScanMode.Interval,
                IntervalSeconds: 900,
                DebounceSeconds: 10,
                StableForSeconds: 120,
                Parallelism: 1,
                TimeoutSeconds: 300,
                StateDirectory: stateRoot,
                LockPath: Path.Combine(stateRoot, "piccompressor.lock"),
                LogPath: Path.Combine(stateRoot, "piccompressor.jsonl"),
                History: false,
                Defaults: new(
                    Quality: 80,
                    ChromaSubsampling: JpegliChromaSubsampling.Subsampling420,
                    ProgressiveLevel: 2,
                    Exif: ExifPolicy.Remove,
                    ColorProfile: ColorProfilePolicy.Preserve,
                    AlphaBackground: "#FFFFFF",
                    Collision: CollisionPolicy.Skip,
                    LargerOutput: LargerOutputPolicy.Discard,
                    MinSavingsPercent: 5,
                    Suffix: "_compressed"),
                Folders:
                [
                    new(
                        Name: "photos",
                        Input: Path.Combine(dataRoot, "photos"),
                        Output: Path.Combine(dataRoot, "compressed"),
                        Recursive: true)
                ]),
            MissingRevision);

    public ConfigurationDocument Save(ScanConfiguration configuration, string expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);

        lock (gate)
        {
            var currentRevision = File.Exists(configurationPath)
                ? RevisionOf(configurationPath)
                : MissingRevision;
            if (!string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal))
            {
                throw new ConfigurationConflictException();
            }

            Validate(configuration);
            var store = new JsonScanConfigurationStore(configurationPath);
            store.Save(configuration);
            return new(store.Load(), RevisionOf(configurationPath));
        }
    }

    public ScanConfigurationResolution Validate(ScanConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>();
        ScanConfigurationResolution resolution;
        try
        {
            resolution = ScanConfigurationResolver.Resolve(configuration, stateRoot, pathComparer);
            errors.AddRange(resolution.Errors);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add(exception.Message);
            resolution = new(null, errors);
        }

        if (configuration.Parallelism is > 4)
        {
            errors.Add("\"parallelism\" must not exceed 4 in the NAS Web service.");
        }

        if (resolution.Configuration is { } resolved)
        {
            foreach (var folder in resolved.Folders)
            {
                RequireBelow(folder.InputPath, dataRoot, $"Folder '{folder.Name}' input", errors);
                if (folder.OutputDirectory is not null)
                {
                    RequireBelow(folder.OutputDirectory, dataRoot, $"Folder '{folder.Name}' output", errors);
                }
                RequireBelow(folder.StatePath, stateRoot, $"Folder '{folder.Name}' state", errors);
            }

            if (resolved.LockPath is { } lockPath)
            {
                RequireBelow(lockPath, stateRoot, "lockPath", errors);
            }

            if (resolved.LogPath is { } logPath)
            {
                RequireBelow(logPath, stateRoot, "logPath", errors);
            }
        }

        if (errors.Count > 0)
        {
            throw new ConfigurationValidationException(errors);
        }

        return resolution;
    }

    private static string NormalizeRoot(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (ContainsSymbolicLink(fullPath))
        {
            throw new ArgumentException(
                "Mounted roots must not contain symbolic links or reparse points.",
                parameterName);
        }

        return fullPath;
    }

    private void RequireBelow(string candidate, string root, string label, List<string> errors)
    {
        var fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (ContainsSymbolicLink(fullCandidate))
        {
            errors.Add($"{label} must not contain symbolic links or reparse points.");
            return;
        }

        if (pathComparer.Equals(fullCandidate, root))
        {
            return;
        }

        var prefix = root + Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(prefix, pathComparison))
        {
            errors.Add($"{label} must be below the mounted /data or /state root.");
        }
    }

    private static bool ContainsSymbolicLink(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("Path must be rooted.", nameof(path));
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsSymbolicLink(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (file.LinkTarget is not null
                || (file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            {
                return true;
            }

            var directory = new DirectoryInfo(path);
            directory.Refresh();
            return directory.LinkTarget is not null
                || (directory.Exists
                    && directory.Attributes.HasFlag(FileAttributes.ReparsePoint));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string RevisionOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
