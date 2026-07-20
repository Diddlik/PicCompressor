using PicCompressor.Domain;

namespace PicCompressor.Gui.Services;

/// <summary>
/// Auftrag der Oberfläche an die Anwendungsschicht. Die Felder entsprechen den normativen
/// Job-Parametern aus docs/requirements.md Abschnitt 6.1; die Übersetzung auf den
/// Application-Anwendungsfall gehört in den Adapter, nicht in die GUI.
/// </summary>
public sealed record CompressionRequest(
    string InputPath,
    CompressionEngineSettings EngineSettings,
    ExifPolicy ExifPolicy,
    ColorProfilePolicy ColorProfilePolicy,
    RgbColor AlphaBackground,
    CollisionPolicy CollisionPolicy,
    LargerOutputPolicy LargerOutputPolicy,
    string? OutputDirectory,
    string Suffix,
    Guid? PredecessorJobId = null);

/// <summary>
/// Fortschritt eines Jobs. <paramref name="Percent"/> bleibt <c>null</c>, solange die Engine
/// keinen belastbaren Wert liefert; Abschnitt 10.2 verbietet geschätzte Prozentwerte.
/// </summary>
public sealed record CompressionProgress(JobStatus Status, double? Percent = null);

public sealed record CompressionBatchProgress(int Index, CompressionProgress Progress);

/// <summary>
/// Endergebnis eines Jobs. Erfolgs- und Fehlerfelder dürfen sich nicht widersprechen
/// (Abschnitt 6.3); <see cref="Validate"/> hält das fest.
/// </summary>
public sealed record CompressionOutcome(
    JobStatus Status,
    string InputPath,
    string? OutputPath,
    long InputSizeBytes,
    long? OutputSizeBytes,
    bool OutputPublished,
    string? WarningText,
    CompressionErrorCategory? ErrorCategory,
    string? ErrorText,
    Guid? JobId = null)
{
    public bool IsSuccess => Status is JobStatus.Succeeded;

    /// <summary>
    /// Wirft, wenn ein Ergebnis in sich widersprüchlich ist. Die GUI übernimmt keine Ergebnisse,
    /// die Erfolg behaupten und zugleich einen Fehler tragen.
    /// </summary>
    public CompressionOutcome Validate()
    {
        if (IsSuccess && ErrorCategory is not null)
        {
            throw new InvalidOperationException(
                "A successful outcome must not carry an error category.");
        }

        if (!IsSuccess && ErrorCategory is null && Status is not JobStatus.Canceled)
        {
            throw new InvalidOperationException(
                "A non-successful outcome must carry an error category.");
        }

        if (OutputPublished && OutputPath is null)
        {
            throw new InvalidOperationException(
                "A published outcome must name the output path.");
        }

        if (!IsSuccess && OutputPublished)
        {
            throw new InvalidOperationException(
                "Only a successful outcome may publish an output file.");
        }

        return this;
    }

    public static CompressionOutcome Failed(
        string inputPath,
        long inputSizeBytes,
        CompressionErrorCategory category,
        string errorText,
        Guid? jobId = null) =>
        new(
            JobStatus.Failed,
            inputPath,
            null,
            inputSizeBytes,
            null,
            false,
            null,
            category,
            errorText,
            jobId);

    public static CompressionOutcome Canceled(string inputPath, long inputSizeBytes) =>
        new(
            JobStatus.Canceled,
            inputPath,
            null,
            inputSizeBytes,
            null,
            false,
            null,
            CompressionErrorCategory.Canceled,
            null);
}
