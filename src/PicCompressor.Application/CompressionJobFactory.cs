using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed record CompressionJobRequest(
    string InputPath,
    CompressionEngineSettings EngineSettings,
    ExifPolicy ExifPolicy,
    ColorProfilePolicy ColorProfilePolicy,
    RgbColor AlphaBackground,
    CollisionPolicy CollisionPolicy = CollisionPolicy.Skip,
    LargerOutputPolicy LargerOutputPolicy = LargerOutputPolicy.Discard,
    string? OutputDirectory = null,
    string Suffix = "_compressed",
    string? ProfileName = null,
    Guid? PredecessorJobId = null,
    int MinimumSavingsPercent = 0);

public sealed class CompressionJobFactory(
    IFileSystem fileSystem,
    IInputImageInspector inputImageInspector,
    InputValidationLimits limits,
    TimeProvider timeProvider)
{
    private readonly OutputPathPlanner outputPathPlanner = new(fileSystem);

    public CompressionJob Create(
        CompressionJobRequest request,
        IReadOnlyCollection<string>? reservedOutputPaths = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSuffix(request.Suffix);

        var inputPath = GetCanonicalPath(request.InputPath, nameof(request.InputPath));
        if (!fileSystem.FileExists(inputPath))
        {
            throw new JobCreationException(
                CompressionErrorCategory.InputNotFound,
                "Input file does not exist or is not readable.");
        }

        var inputImageInfo = InspectInput(inputPath);
        if (inputImageInfo.FileSizeBytes > limits.MaxFileSizeBytes
            || inputImageInfo.PixelCount > limits.MaxPixelCount)
        {
            throw new JobCreationException(
                CompressionErrorCategory.LimitExceeded,
                "Input file exceeds the configured file-size or pixel limit.");
        }

        var outputDirectory = request.OutputDirectory is null
            ? Path.GetDirectoryName(inputPath)!
            : GetCanonicalPath(request.OutputDirectory, nameof(request.OutputDirectory));
        var inputName = Path.GetFileNameWithoutExtension(inputPath);
        var desiredOutputPath = GetCanonicalPath(
            Path.Combine(outputDirectory, $"{inputName}{request.Suffix}.jpg"),
            nameof(request.OutputDirectory));
        var outputPath = outputPathPlanner.Plan(
            desiredOutputPath,
            request.CollisionPolicy,
            reservedOutputPaths);

        if (fileSystem.PathsEqual(inputPath, outputPath)
            && request.CollisionPolicy is not CollisionPolicy.Overwrite)
        {
            throw new JobCreationException(
                CompressionErrorCategory.OutputConflict,
                "Output path equals input path without explicit overwrite permission.");
        }

        return new CompressionJob(
            Guid.NewGuid(),
            inputPath,
            outputPath,
            request.EngineSettings,
            request.ExifPolicy,
            request.ColorProfilePolicy,
            request.AlphaBackground,
            request.CollisionPolicy,
            request.LargerOutputPolicy,
            timeProvider.GetUtcNow(),
            inputImageInfo,
            request.ProfileName,
            request.PredecessorJobId,
            request.MinimumSavingsPercent);
    }

    private InputImageInfo InspectInput(string inputPath)
    {
        try
        {
            return inputImageInspector.Inspect(inputPath);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException
                or UnauthorizedAccessException)
        {
            throw new JobCreationException(
                CompressionErrorCategory.InputNotFound,
                "Input file does not exist or is not readable.",
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw new JobCreationException(
                CompressionErrorCategory.UnsupportedInput,
                "Input is not a structurally valid JPEG or PNG file.",
                exception);
        }
        catch (IOException exception)
        {
            throw new JobCreationException(
                CompressionErrorCategory.FileSystemError,
                "Input file could not be read.",
                exception);
        }
    }

    private static void ValidateSuffix(string suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);

        if (suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new JobCreationException(
                CompressionErrorCategory.InvalidArguments,
                "Output suffix contains invalid filename characters.");
        }
    }

    private string GetCanonicalPath(string path, string parameterName)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
            return fileSystem.GetCanonicalPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw new JobCreationException(
                CompressionErrorCategory.InvalidArguments,
                $"Invalid path: {parameterName}.",
                exception);
        }
    }
}
