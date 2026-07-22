using PicCompressor.Domain;

namespace PicCompressor.Application;

/// <summary>
/// Persistierte Benutzereinstellungen (Abschnitt 13.2).
/// </summary>
/// <remarks>
/// Die Werte dieses Datensatzes sind bereits geprüft: der Speicher ersetzt
/// unbekannte oder beschädigte Angaben feldweise durch den sicheren Vorgabewert,
/// statt die gesamte Datei zu verwerfen oder den Start scheitern zu lassen.
///
/// <see cref="Language"/> und <see cref="Theme"/> sind bewusst Zeichenketten:
/// die zugehörigen Aufzählungen sind Darstellungsdetails der GUI, und die
/// Application-Schicht kennt keine Oberflächenbegriffe. Die CLI ignoriert beide
/// Felder (Abschnitt 11.2).
/// </remarks>
public sealed record ApplicationSettings
{
    /// <summary>
    /// Version des Dateiformats. Änderungen sind additiv; eine nicht
    /// abwärtskompatible Änderung erhöht die Version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Language { get; init; } = "System";

    public string Theme { get; init; } = "System";

    public string EngineId { get; init; } = JpegliSettings.JpegliEngineId;

    public int Quality { get; init; } = 90;

    public JpegliChromaSubsampling ChromaSubsampling { get; init; } =
        JpegliChromaSubsampling.Subsampling420;

    public int ProgressiveLevel { get; init; } = 2;

    public ExifPolicy ExifPolicy { get; init; } = ExifPolicy.Remove;

    public ColorProfilePolicy ColorProfilePolicy { get; init; } =
        ColorProfilePolicy.Preserve;

    public CollisionPolicy CollisionPolicy { get; init; } = CollisionPolicy.Skip;

    public LargerOutputPolicy LargerOutputPolicy { get; init; } =
        LargerOutputPolicy.Discard;

    public string Suffix { get; init; } = "_compressed";

    public string? OutputDirectory { get; init; }

    public int ParallelJobs { get; init; } = 1;

    /// <summary>Aufbewahrungsdauer des Verlaufs in Tagen (Abschnitt 13.1).</summary>
    public int HistoryRetentionDays { get; init; } = 90;

    /// <summary>Maximale Größe einer Logdatei in MB vor der Rotation (Abschnitt 13.3).</summary>
    public int LogMaxFileMegabytes { get; init; } = 5;

    /// <summary>Anzahl aufbewahrter Loggenerationen (Abschnitt 13.3).</summary>
    public int LogRetainedFiles { get; init; } = 5;

    /// <summary>
    /// Enginespezifisches Encoder-Zeitlimit in Sekunden (MP-004, Abschnitt 7.1).
    /// <c>0</c> bedeutet „kein Limit“ und erhält das heutige Verhalten.
    /// </summary>
    public int JpegliTimeoutSeconds { get; init; }

    /// <summary>Encoder-Zeitlimit für Guetzli in Sekunden; <c>0</c> = kein Limit (MP-004).</summary>
    public int GuetzliTimeoutSeconds { get; init; }
}

public interface IApplicationSettingsStore
{
    /// <summary>
    /// Lädt die Einstellungen. Fehlt die Datei oder ist sie unlesbar, liefert der
    /// Speicher sichere Vorgabewerte statt einer Ausnahme (Abschnitt 13.2).
    /// </summary>
    ApplicationSettings Load();

    void Save(ApplicationSettings settings);
}

/// <summary>
/// Hält Einstellungen nur für die laufende Sitzung. Vorgabe, solange kein
/// persistenter Speicher verdrahtet ist, und Ersatz in Tests.
/// </summary>
public sealed class InMemoryApplicationSettingsStore : IApplicationSettingsStore
{
    private ApplicationSettings current = new();

    public ApplicationSettings Load() => current;

    public void Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        current = settings;
    }
}
