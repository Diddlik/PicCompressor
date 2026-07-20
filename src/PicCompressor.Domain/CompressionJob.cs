namespace PicCompressor.Domain;

public sealed class CompressionJob
{
    public CompressionJob(
        Guid id,
        string inputPath,
        string outputPath,
        CompressionEngineSettings engineSettings,
        ExifPolicy exifPolicy,
        ColorProfilePolicy colorProfilePolicy,
        RgbColor alphaBackground,
        CollisionPolicy collisionPolicy,
        LargerOutputPolicy largerOutputPolicy,
        DateTimeOffset createdAt,
        InputImageInfo inputImageInfo,
        string? profileName = null,
        Guid? predecessorJobId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Job ID must not be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(engineSettings);
        ArgumentNullException.ThrowIfNull(inputImageInfo);

        if (!Path.IsPathFullyQualified(inputPath))
        {
            throw new ArgumentException("Input path must be fully qualified.", nameof(inputPath));
        }

        if (!Path.IsPathFullyQualified(outputPath))
        {
            throw new ArgumentException("Output path must be fully qualified.", nameof(outputPath));
        }

        Id = id;
        InputPath = inputPath;
        OutputPath = outputPath;
        EngineSettings = engineSettings;
        ExifPolicy = exifPolicy;
        ColorProfilePolicy = colorProfilePolicy;
        AlphaBackground = alphaBackground;
        CollisionPolicy = collisionPolicy;
        LargerOutputPolicy = largerOutputPolicy;
        CreatedAt = createdAt;
        InputImageInfo = inputImageInfo;
        ProfileName = profileName;
        PredecessorJobId = predecessorJobId;
    }

    public Guid Id { get; }
    public string InputPath { get; }
    public string OutputPath { get; }
    public CompressionEngineSettings EngineSettings { get; }
    public int Quality => EngineSettings.Quality;
    public ExifPolicy ExifPolicy { get; }
    public ColorProfilePolicy ColorProfilePolicy { get; }
    public RgbColor AlphaBackground { get; }
    public CollisionPolicy CollisionPolicy { get; }
    public LargerOutputPolicy LargerOutputPolicy { get; }
    public DateTimeOffset CreatedAt { get; }
    public InputImageInfo InputImageInfo { get; }
    public string? ProfileName { get; }
    public Guid? PredecessorJobId { get; }
}
