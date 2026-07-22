using PicCompressor.Domain;

namespace PicCompressor.Application;

public enum OutputPublicationDisposition
{
    Published,
    DiscardedNotSmaller,
    DiscardedBelowMinimumSavings
}

public sealed record OutputPublicationResult(
    OutputPublicationDisposition Disposition,
    long EncodedSizeBytes);

public sealed class TemporaryOutputFile
{
    internal TemporaryOutputFile(string path, string targetPath)
    {
        Path = path;
        TargetPath = targetPath;
    }

    public string Path { get; }

    internal string TargetPath { get; }
}

public sealed class SafeOutputPublisher(
    IOutputFileSystem fileSystem,
    IInputImageInspector imageInspector)
{
    public TemporaryOutputFile CreateTemporaryFile(CompressionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var path = fileSystem.GetCanonicalPath(fileSystem.CreateTemporaryFile(job.OutputPath));
        return new(path, job.OutputPath);
    }

    public OutputPublicationResult Publish(
        CompressionJob job,
        TemporaryOutputFile temporaryOutput)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(temporaryOutput);

        var canonicalTemporaryPath = temporaryOutput.Path;
        var temporaryDirectory = Path.GetDirectoryName(canonicalTemporaryPath)!;
        var targetDirectory = Path.GetDirectoryName(job.OutputPath)!;
        if (!fileSystem.PathsEqual(temporaryOutput.TargetPath, job.OutputPath)
            || !fileSystem.PathsEqual(temporaryDirectory, targetDirectory))
        {
            throw new OutputPublicationException(
                CompressionErrorCategory.InvalidArguments,
                "Temporary output must be located in the target directory.");
        }

        if (!fileSystem.FileExists(canonicalTemporaryPath))
        {
            throw new OutputPublicationException(
                CompressionErrorCategory.OutputValidationFailed,
                "Temporary output does not exist.");
        }

        var outputInfo = InspectOutput(canonicalTemporaryPath);
        if (outputInfo.Format is not InputImageFormat.Jpeg
            || outputInfo.Width != job.InputImageInfo.Width
            || outputInfo.Height != job.InputImageInfo.Height)
        {
            throw Failure(
                CompressionErrorCategory.OutputValidationFailed,
                "Temporary output is not a JPEG with the expected dimensions.",
                canonicalTemporaryPath);
        }

        // Mindesteinsparung (MP-004): ein Ergebnis unterhalb der geforderten Prozentgrenze wird
        // verworfen. Nur aktiv bei einem positiven Wert, damit der Standard 0 das bisherige
        // Verhalten (nur die Nicht-kleiner-Richtlinie greift) unverändert lässt.
        if (job.MinimumSavingsPercent > 0)
        {
            var inputSize = job.InputImageInfo.FileSizeBytes;
            var savedPercent = inputSize > 0
                ? (inputSize - outputInfo.FileSizeBytes) * 100d / inputSize
                : 0d;
            if (savedPercent < job.MinimumSavingsPercent)
            {
                DeleteRequired(canonicalTemporaryPath);
                return new(
                    OutputPublicationDisposition.DiscardedBelowMinimumSavings,
                    outputInfo.FileSizeBytes);
            }
        }

        if (outputInfo.FileSizeBytes >= job.InputImageInfo.FileSizeBytes
            && job.LargerOutputPolicy is LargerOutputPolicy.Discard)
        {
            DeleteRequired(canonicalTemporaryPath);
            return new(
                OutputPublicationDisposition.DiscardedNotSmaller,
                outputInfo.FileSizeBytes);
        }

        var overwrite = job.CollisionPolicy is CollisionPolicy.Overwrite;
        if (!overwrite && fileSystem.FileExists(job.OutputPath))
        {
            throw Failure(
                CompressionErrorCategory.OutputConflict,
                "Output path became occupied before publication.",
                canonicalTemporaryPath);
        }

        try
        {
            fileSystem.MoveFile(canonicalTemporaryPath, job.OutputPath, overwrite);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var category = !overwrite && fileSystem.FileExists(job.OutputPath)
                ? CompressionErrorCategory.OutputConflict
                : CompressionErrorCategory.FileSystemError;
            throw Failure(
                category,
                "Temporary output could not be published.",
                canonicalTemporaryPath,
                exception);
        }

        if (!fileSystem.FileExists(job.OutputPath)
            || fileSystem.FileExists(canonicalTemporaryPath))
        {
            throw new OutputPublicationException(
                CompressionErrorCategory.FileSystemError,
                "Output publication did not produce the expected filesystem state.");
        }

        return new(OutputPublicationDisposition.Published, outputInfo.FileSizeBytes);
    }

    public void Discard(TemporaryOutputFile temporaryOutput)
    {
        ArgumentNullException.ThrowIfNull(temporaryOutput);
        DeleteRequired(temporaryOutput.Path);
    }

    private InputImageInfo InspectOutput(string temporaryPath)
    {
        try
        {
            return imageInspector.Inspect(temporaryPath);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or UnauthorizedAccessException)
        {
            throw Failure(
                CompressionErrorCategory.OutputValidationFailed,
                "Temporary output is not a structurally valid JPEG.",
                temporaryPath,
                exception);
        }
    }

    private OutputPublicationException Failure(
        CompressionErrorCategory category,
        string message,
        string temporaryPath,
        Exception? cause = null)
    {
        try
        {
            if (fileSystem.FileExists(temporaryPath))
            {
                fileSystem.DeleteFile(temporaryPath);
            }

            if (fileSystem.FileExists(temporaryPath))
            {
                return new(
                    CompressionErrorCategory.FileSystemError,
                    "Temporary output still exists after cleanup.",
                    cause);
            }
        }
        catch (Exception cleanupException) when (
            cleanupException is IOException or UnauthorizedAccessException)
        {
            return new(
                CompressionErrorCategory.FileSystemError,
                "Temporary output cleanup failed.",
                cause is null
                    ? cleanupException
                    : new AggregateException(cause, cleanupException));
        }

        return new(category, message, cause);
    }

    private void DeleteRequired(string temporaryPath)
    {
        try
        {
            fileSystem.DeleteFile(temporaryPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new OutputPublicationException(
                CompressionErrorCategory.FileSystemError,
                "Temporary output cleanup failed.",
                exception);
        }

        if (fileSystem.FileExists(temporaryPath))
        {
            throw new OutputPublicationException(
                CompressionErrorCategory.FileSystemError,
                "Temporary output still exists after cleanup.");
        }
    }
}
