using System.Text.Json;
using System.Text.Json.Serialization;
using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Infrastructure;

/// <summary>
/// Einstellungen als eine versionierte JSON-Datei im Benutzer-Konfigurationsverzeichnis
/// (Abschnitt 13.2).
/// </summary>
public sealed class JsonApplicationSettingsStore(
    string path,
    IDiagnosticLog? diagnosticLog = null) : IApplicationSettingsStore
{
    private const string Component = "Settings";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    private readonly string path = Path.GetFullPath(
        !string.IsNullOrWhiteSpace(path)
            ? path
            : throw new ArgumentException("Path is required.", nameof(path)));

    private readonly IDiagnosticLog log = diagnosticLog ?? NullDiagnosticLog.Instance;

    public ApplicationSettings Load()
    {
        if (!File.Exists(path))
        {
            return new ApplicationSettings();
        }

        ApplicationSettings? stored;
        try
        {
            stored = JsonSerializer.Deserialize<ApplicationSettings>(
                File.ReadAllText(path),
                SerializerOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Eine beschädigte Konfiguration darf den Start nicht verhindern; sie wird
            // gemeldet und durch sichere Vorgabewerte ersetzt (Abschnitt 13.2).
            Report($"Settings could not be read and were replaced by defaults: {exception.Message}");
            return new ApplicationSettings();
        }

        if (stored is null)
        {
            Report("Settings file was empty and was replaced by defaults.");
            return new ApplicationSettings();
        }

        if (stored.SchemaVersion > ApplicationSettings.CurrentSchemaVersion)
        {
            // Eine neuere Version wird nicht heruntergestuft, sonst gingen unbekannte
            // Felder beim naechsten Speichern verloren.
            Report(
                $"Settings schema version {stored.SchemaVersion} is newer than the supported "
                + $"version {ApplicationSettings.CurrentSchemaVersion}; defaults are used.");
            return new ApplicationSettings();
        }

        return Sanitize(stored);
    }

    public void Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var temporaryPath = $"{path}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Erst vollstaendig schreiben, dann ersetzen: ein Abbruch darf keine halbe
            // Konfigurationsdatei hinterlassen.
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    settings with { SchemaVersion = ApplicationSettings.CurrentSchemaVersion },
                    SerializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Report($"Settings could not be saved: {exception.Message}");
            TryDelete(temporaryPath);
        }
    }

    /// <summary>
    /// Ersetzt unzulaessige Werte feldweise durch den Vorgabewert. Ein einzelnes
    /// unsinniges Feld darf nicht die uebrigen Einstellungen verwerfen.
    /// </summary>
    private ApplicationSettings Sanitize(ApplicationSettings stored)
    {
        var defaults = new ApplicationSettings();
        var corrections = new List<string>();

        var quality = InRange(stored.Quality, 1, 100, defaults.Quality, "Quality", corrections);
        var progressive = InRange(
            stored.ProgressiveLevel, 0, 2, defaults.ProgressiveLevel, "ProgressiveLevel", corrections);
        var parallel = InRange(
            stored.ParallelJobs, 1, 256, defaults.ParallelJobs, "ParallelJobs", corrections);
        var retention = InRange(
            stored.HistoryRetentionDays, 1, 3650, defaults.HistoryRetentionDays,
            "HistoryRetentionDays", corrections);
        var logSize = InRange(
            stored.LogMaxFileMegabytes, 1, 1024, defaults.LogMaxFileMegabytes,
            "LogMaxFileMegabytes", corrections);
        var logFiles = InRange(
            stored.LogRetainedFiles, 1, 100, defaults.LogRetainedFiles,
            "LogRetainedFiles", corrections);
        // 0 = kein Limit; Obergrenze 24 Stunden (MP-004).
        var jpegliTimeout = InRange(
            stored.JpegliTimeoutSeconds, 0, 86_400, defaults.JpegliTimeoutSeconds,
            "JpegliTimeoutSeconds", corrections);
        var guetzliTimeout = InRange(
            stored.GuetzliTimeoutSeconds, 0, 86_400, defaults.GuetzliTimeoutSeconds,
            "GuetzliTimeoutSeconds", corrections);

        var sanitized = stored with
        {
            SchemaVersion = ApplicationSettings.CurrentSchemaVersion,
            Quality = quality,
            ProgressiveLevel = progressive,
            ParallelJobs = parallel,
            HistoryRetentionDays = retention,
            LogMaxFileMegabytes = logSize,
            LogRetainedFiles = logFiles,
            JpegliTimeoutSeconds = jpegliTimeout,
            GuetzliTimeoutSeconds = guetzliTimeout,
            EngineId = string.IsNullOrWhiteSpace(stored.EngineId)
                ? Corrected(defaults.EngineId, "EngineId", corrections)
                : stored.EngineId,
            Suffix = string.IsNullOrWhiteSpace(stored.Suffix)
                ? Corrected(defaults.Suffix, "Suffix", corrections)
                : stored.Suffix,
            ChromaSubsampling = Defined(
                stored.ChromaSubsampling, defaults.ChromaSubsampling,
                "ChromaSubsampling", corrections),
            ExifPolicy = Defined(
                stored.ExifPolicy, defaults.ExifPolicy, "ExifPolicy", corrections),
            ColorProfilePolicy = Defined(
                stored.ColorProfilePolicy, defaults.ColorProfilePolicy,
                "ColorProfilePolicy", corrections),
            CollisionPolicy = Defined(
                stored.CollisionPolicy, defaults.CollisionPolicy,
                "CollisionPolicy", corrections),
            LargerOutputPolicy = Defined(
                stored.LargerOutputPolicy, defaults.LargerOutputPolicy,
                "LargerOutputPolicy", corrections)
        };

        if (corrections.Count > 0)
        {
            Report($"Settings fields replaced by defaults: {string.Join(", ", corrections)}.");
        }

        return sanitized;
    }

    private static int InRange(
        int value, int minimum, int maximum, int fallback, string name, List<string> corrections)
    {
        if (value >= minimum && value <= maximum)
        {
            return value;
        }

        corrections.Add(name);
        return fallback;
    }

    private static TEnum Defined<TEnum>(
        TEnum value, TEnum fallback, string name, List<string> corrections)
        where TEnum : struct, Enum
    {
        if (Enum.IsDefined(value))
        {
            return value;
        }

        corrections.Add(name);
        return fallback;
    }

    private static string Corrected(string fallback, string name, List<string> corrections)
    {
        corrections.Add(name);
        return fallback;
    }

    private void Report(string message) =>
        log.Write(
            new DiagnosticEntry(
                DateTimeOffset.UtcNow,
                DiagnosticSeverity.Warning,
                Component,
                message));

    private static void TryDelete(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Ein liegengebliebener Zwischenstand ist kein Produktfehler.
        }
    }
}
