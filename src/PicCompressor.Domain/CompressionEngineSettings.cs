namespace PicCompressor.Domain;

public abstract class CompressionEngineSettings
{
    protected CompressionEngineSettings(string engineId, int quality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        EngineId = engineId;
        Quality = quality;
    }

    public string EngineId { get; }
    public int Quality { get; }
}

public enum JpegliChromaSubsampling
{
    Subsampling444,
    Subsampling440,
    Subsampling422,
    Subsampling420
}

public sealed class JpegliSettings : CompressionEngineSettings
{
    public const string JpegliEngineId = "jpegli";

    public JpegliSettings(
        int quality,
        JpegliChromaSubsampling chromaSubsampling,
        int progressiveLevel)
        : base(JpegliEngineId, quality)
    {
        if (quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(quality));
        }

        if (!Enum.IsDefined(chromaSubsampling))
        {
            throw new ArgumentOutOfRangeException(nameof(chromaSubsampling));
        }

        if (progressiveLevel is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(progressiveLevel));
        }

        ChromaSubsampling = chromaSubsampling;
        ProgressiveLevel = progressiveLevel;
    }

    public JpegliChromaSubsampling ChromaSubsampling { get; }
    public int ProgressiveLevel { get; }
}
