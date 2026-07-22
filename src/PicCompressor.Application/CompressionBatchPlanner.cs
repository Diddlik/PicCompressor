using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed record CompressionBatchSettings(
    CompressionEngineSettings EngineSettings,
    ExifPolicy ExifPolicy,
    ColorProfilePolicy ColorProfilePolicy,
    RgbColor AlphaBackground,
    CollisionPolicy CollisionPolicy,
    LargerOutputPolicy LargerOutputPolicy,
    string? OutputDirectory,
    string Suffix,
    int MinimumSavingsPercent = 0);

public sealed record CompressionJobPlan(
    DiscoveredInput Input,
    CompressionJob? Job,
    CompressionErrorCategory? ErrorCategory,
    string? ErrorText);

public sealed class CompressionBatchPlanner(CompressionJobFactory jobFactory)
{
    public IReadOnlyList<CompressionJobPlan> Plan(
        IReadOnlyList<DiscoveredInput> inputs,
        CompressionBatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(settings);
        var plans = new List<CompressionJobPlan>(inputs.Count);
        var reservedOutputPaths = new List<string>(inputs.Count);

        foreach (var input in inputs)
        {
            try
            {
                var outputDirectory = GetOutputDirectory(settings.OutputDirectory, input.RelativeDirectory);
                var job = jobFactory.Create(
                    new(
                        input.Path,
                        settings.EngineSettings,
                        settings.ExifPolicy,
                        settings.ColorProfilePolicy,
                        settings.AlphaBackground,
                        settings.CollisionPolicy,
                        settings.LargerOutputPolicy,
                        outputDirectory,
                        settings.Suffix,
                        MinimumSavingsPercent: settings.MinimumSavingsPercent),
                    reservedOutputPaths);
                reservedOutputPaths.Add(job.OutputPath);
                plans.Add(new(input, job, null, null));
            }
            catch (JobCreationException exception)
            {
                plans.Add(new(input, null, exception.Category, exception.Message));
            }
        }

        return plans;
    }

    private static string? GetOutputDirectory(string? outputDirectory, string relativeDirectory)
    {
        if (outputDirectory is null || relativeDirectory.Length == 0)
        {
            return outputDirectory;
        }

        if (Path.IsPathFullyQualified(relativeDirectory)
            || relativeDirectory.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Contains("..", StringComparer.Ordinal))
        {
            throw new JobCreationException(
                CompressionErrorCategory.InvalidArguments,
                "Relative input directory escapes the output directory.");
        }

        return Path.Combine(outputDirectory, relativeDirectory);
    }
}
