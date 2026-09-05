using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed record CompressionProfile
{
    public string Name { get; init; } = "";
    public int Quality { get; init; } = 90;
    public JpegliChromaSubsampling ChromaSubsampling { get; init; } = JpegliChromaSubsampling.Subsampling420;
    public int ProgressiveLevel { get; init; } = 2;
    public ExifPolicy ExifPolicy { get; init; } = ExifPolicy.Remove;
    public ColorProfilePolicy ColorProfilePolicy { get; init; } = ColorProfilePolicy.Preserve;
    public RgbColor AlphaBackground { get; init; } = RgbColor.White;
    public string Suffix { get; init; } = "_compressed";
    public string? OutputDirectory { get; init; }
    public bool OverwriteOriginal { get; init; }
    public CollisionPolicy CollisionPolicy { get; init; } = CollisionPolicy.Skip;
    public LargerOutputPolicy LargerOutputPolicy { get; init; } = LargerOutputPolicy.Discard;
    public int ParallelJobs { get; init; } = 1;
    public int JpegliTimeoutSeconds { get; init; }
    public int MinimumSavingsPercent { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name != Name.Trim() || Name.Length > 80
            || Name.Any(char.IsControl))
        {
            throw new ArgumentException("Profile names must contain 1-80 characters without control characters or surrounding whitespace.");
        }

        _ = new JpegliSettings(Quality, ChromaSubsampling, ProgressiveLevel);
        if (!Enum.IsDefined(ExifPolicy) || !Enum.IsDefined(ColorProfilePolicy)
            || !Enum.IsDefined(CollisionPolicy) || !Enum.IsDefined(LargerOutputPolicy)
            || ParallelJobs is < 1 or > 256 || JpegliTimeoutSeconds is < 0 or > 86400
            || MinimumSavingsPercent is < 0 or > 99)
        {
            throw new ArgumentException("Invalid profile settings.");
        }

        if (string.IsNullOrWhiteSpace(Suffix) || Suffix.Any(char.IsControl)
            || Suffix.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            throw new ArgumentException("Invalid output suffix.");
        }

        if (OutputDirectory is not null
            && (string.IsNullOrWhiteSpace(OutputDirectory) || !Path.IsPathFullyQualified(OutputDirectory)))
        {
            throw new ArgumentException("Profile output directories must be absolute paths.");
        }

        if (OverwriteOriginal && (OutputDirectory is not null || CollisionPolicy != CollisionPolicy.Overwrite))
        {
            throw new ArgumentException("Replacing originals requires overwrite permission and no output directory.");
        }
    }
}

public interface ICompressionProfileStore
{
    IReadOnlyList<CompressionProfile> Load();
    void Save(IReadOnlyList<CompressionProfile> profiles);
}

public sealed class InMemoryCompressionProfileStore : ICompressionProfileStore
{
    private CompressionProfile[] profiles = [];
    public IReadOnlyList<CompressionProfile> Load() => profiles.ToArray();
    public void Save(IReadOnlyList<CompressionProfile> profiles) => this.profiles = profiles.ToArray();
}
