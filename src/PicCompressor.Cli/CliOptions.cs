using PicCompressor.Domain;

namespace PicCompressor.Cli;

internal sealed record CliOptions(
    IReadOnlyList<string> InputPaths,
    string EngineId,
    int Quality,
    string? OutputDirectory,
    string Suffix,
    CollisionPolicy CollisionPolicy,
    LargerOutputPolicy LargerOutputPolicy,
    ExifPolicy ExifPolicy,
    ColorProfilePolicy ColorProfilePolicy,
    bool Recursive,
    bool DryRun,
    int Parallelism,
    bool Json,
    bool NoHistory,
    string? LogPath,
    int TimeoutSeconds)
{
    internal static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var inputPaths = new List<string>();
        var engineId = JpegliSettings.JpegliEngineId;
        string? outputDirectory = null;
        var quality = 80;
        var qualityExplicit = false;
        var suffix = "_compressed";
        var collisionPolicy = CollisionPolicy.Skip;
        var largerOutputPolicy = LargerOutputPolicy.Discard;
        var exifPolicy = ExifPolicy.Remove;
        var colorProfilePolicy = ColorProfilePolicy.Preserve;
        var recursive = false;
        var dryRun = false;
        var parallelism = 1;
        var json = false;
        var noHistory = false;
        string? logPath = null;
        var timeoutSeconds = 0;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--json":
                    json = true;
                    break;
                case "--recursive":
                    recursive = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--no-history":
                    noHistory = true;
                    break;
                case "--log":
                    logPath = NextValue(args, ref index, "--log");
                    break;
                case "--parallelism":
                    parallelism = ParseParallelism(NextValue(args, ref index, "--parallelism"));
                    break;
                case "--timeout":
                    timeoutSeconds = ParseTimeout(NextValue(args, ref index, "--timeout"));
                    break;
                case "--engine":
                    engineId = ParseEngine(NextValue(args, ref index, "--engine"));
                    break;
                case "--quality":
                    quality = ParseQuality(NextValue(args, ref index, "--quality"));
                    qualityExplicit = true;
                    break;
                case "--output-dir":
                    outputDirectory = NextValue(args, ref index, "--output-dir");
                    break;
                case "--suffix":
                    suffix = NextValue(args, ref index, "--suffix");
                    break;
                case "--collision":
                    collisionPolicy = ParseEnum<CollisionPolicy>(
                        NextValue(args, ref index, "--collision"),
                        "--collision");
                    break;
                case "--larger-output":
                    largerOutputPolicy = ParseEnum<LargerOutputPolicy>(
                        NextValue(args, ref index, "--larger-output"),
                        "--larger-output");
                    break;
                case "--exif":
                    exifPolicy = ParseEnum<ExifPolicy>(
                        NextValue(args, ref index, "--exif"),
                        "--exif");
                    break;
                case "--color-profile":
                    colorProfilePolicy = ParseEnum<ColorProfilePolicy>(
                        NextValue(args, ref index, "--color-profile"),
                        "--color-profile");
                    break;
                default:
                    if (args[index].StartsWith('-'))
                    {
                        throw new CliUsageException($"Unknown option: {args[index]}");
                    }

                    inputPaths.Add(args[index]);
                    break;
            }
        }

        // Guetzli's effective quality floor follows its revision (Abschnitt 5.2). An
        // unset quality defaults up to the floor; an explicit value below it is a
        // usage error rather than a silent change.
        if (engineId == GuetzliSettings.GuetzliEngineId)
        {
            if (!qualityExplicit)
            {
                quality = GuetzliSettings.MinimumQuality;
            }
            else if (quality < GuetzliSettings.MinimumQuality)
            {
                throw new CliUsageException(
                    $"--engine guetzli requires --quality {GuetzliSettings.MinimumQuality} or higher.");
            }
        }

        return new(
            inputPaths.Count > 0
                ? inputPaths
                : throw new CliUsageException("At least one input path is required."),
            engineId,
            quality,
            outputDirectory,
            suffix,
            collisionPolicy,
            largerOutputPolicy,
            exifPolicy,
            colorProfilePolicy,
            recursive,
            dryRun,
            parallelism,
            json,
            noHistory,
            logPath,
            timeoutSeconds);
    }

    private static string ParseEngine(string value)
    {
        var normalized = value.ToLowerInvariant();
        return normalized == JpegliSettings.JpegliEngineId
            || normalized == GuetzliSettings.GuetzliEngineId
            ? normalized
            : throw new CliUsageException(
                $"--engine must be {JpegliSettings.JpegliEngineId} or {GuetzliSettings.GuetzliEngineId}.");
    }

    private static int ParseQuality(string value) =>
        int.TryParse(value, out var quality) && quality is >= 1 and <= 100
            ? quality
            : throw new CliUsageException("--quality must be an integer from 1 to 100.");

    private static int ParseParallelism(string value) =>
        int.TryParse(value, out var parallelism) && parallelism is >= 1 and <= 256
            ? parallelism
            : throw new CliUsageException("--parallelism must be an integer from 1 to 256.");

    // 0 = kein Zeitlimit (MP-004); Obergrenze 24 Stunden.
    private static int ParseTimeout(string value) =>
        int.TryParse(value, out var seconds) && seconds is >= 0 and <= 86_400
            ? seconds
            : throw new CliUsageException("--timeout must be an integer from 0 to 86400 seconds (0 = no limit).");

    private static T ParseEnum<T>(string value, string option)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var result) && Enum.IsDefined(result)
            ? result
            : throw new CliUsageException($"Invalid value for {option}: {value}");

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || args[index].StartsWith('-'))
        {
            throw new CliUsageException($"Missing value for {option}.");
        }

        return args[index];
    }
}

internal sealed class CliUsageException(string message) : Exception(message);
