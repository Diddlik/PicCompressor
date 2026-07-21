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

/// <summary>
/// Einstellungen der optionalen Legacy-Engine Guetzli (Abschnitt 5.2). Guetzli erzeugt nur
/// sequenzielle JPEGs und kennt keine Chroma- oder Progressive-Wahl; steuerbar ist allein die
/// Qualität. Die Untergrenze folgt der offiziellen Revision; der reale Wert stammt aus der
/// Engine-Capability, sobald der Wrapper die Bibliothek einbindet.
/// </summary>
public sealed class GuetzliSettings : CompressionEngineSettings
{
    public const string GuetzliEngineId = "guetzli";

    /// <summary>Untergrenze der offiziellen Guetzli-Revision (Abschnitt 5.2).</summary>
    public const int MinimumQuality = 84;

    public GuetzliSettings(int quality)
        : base(GuetzliEngineId, quality)
    {
        if (quality is < MinimumQuality or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(quality));
        }
    }
}
