using System.Globalization;
using PicCompressor.Domain;

namespace PicCompressor.Application;

/// <summary>
/// Taktgeber des Dauerbetriebs (D-056). <see cref="Interval"/> scannt jeden Ordner nach Ablauf
/// der Wartezeit vollständig, <see cref="Watch"/> zusätzlich nach entprellten Dateisystem-
/// ereignissen.
/// </summary>
public enum ScanMode
{
    Interval,
    Watch
}

/// <summary>
/// Kompressionseinstellungen aus der Konfigurationsdatei (D-055). Jedes Feld ist optional: ein
/// Ordner erbt jeden nicht gesetzten Wert aus dem globalen <c>defaults</c>-Block.
/// </summary>
public sealed record ScanCompressionSettings(
    int? Quality = null,
    JpegliChromaSubsampling? ChromaSubsampling = null,
    int? ProgressiveLevel = null,
    ExifPolicy? Exif = null,
    ColorProfilePolicy? ColorProfile = null,
    string? AlphaBackground = null,
    CollisionPolicy? Collision = null,
    LargerOutputPolicy? LargerOutput = null,
    int? MinSavingsPercent = null,
    string? Suffix = null);

/// <summary>
/// Ein überwachter Ordner. <see cref="Name"/> benennt ihn in Meldungen und bildet den Vorgabenamen
/// seiner Zustandsdatei, weshalb er auf dateinamenstaugliche Zeichen begrenzt ist.
/// </summary>
public sealed record ScanFolderConfiguration(
    string? Name = null,
    string? Input = null,
    string? Output = null,
    bool? Recursive = null,
    string? StatePath = null,
    ScanCompressionSettings? Settings = null);

/// <summary>
/// Die gelesene Konfigurationsdatei des Dauerbetriebs (D-055). Sie wird strikt geprüft: ein
/// unzulässiger Wert ist ein Nutzungsfehler und wird nicht still durch einen Vorgabewert ersetzt,
/// weil ein unbeaufsichtigter Lauf sonst dauerhaft anders arbeitet als konfiguriert.
/// </summary>
public sealed record ScanConfiguration(
    int SchemaVersion = 0,
    ScanMode? Mode = null,
    int? IntervalSeconds = null,
    int? DebounceSeconds = null,
    int? StableForSeconds = null,
    int? Parallelism = null,
    int? TimeoutSeconds = null,
    string? StateDirectory = null,
    string? LockPath = null,
    string? LogPath = null,
    bool? History = null,
    ScanCompressionSettings? Defaults = null,
    IReadOnlyList<ScanFolderConfiguration>? Folders = null)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Ein geprüfter Ordner mit seinen wirksamen Einstellungen.</summary>
public sealed record ResolvedScanFolder(
    string Name,
    string InputPath,
    string OutputDirectory,
    bool Recursive,
    string StatePath,
    CompressionBatchSettings Settings);

/// <summary>Die geprüfte Konfiguration; jeder Wert ist gesetzt und liegt im zulässigen Bereich.</summary>
public sealed record ResolvedScanConfiguration(
    ScanMode Mode,
    TimeSpan Interval,
    TimeSpan Debounce,
    int StableForSeconds,
    int Parallelism,
    int TimeoutSeconds,
    string? LockPath,
    string? LogPath,
    bool History,
    IReadOnlyList<ResolvedScanFolder> Folders);

/// <summary>
/// Ergebnis der Prüfung. <see cref="Configuration"/> ist genau dann gesetzt, wenn
/// <see cref="Errors"/> leer ist; alle Fehler werden gesammelt, damit ein Lauf nicht bei jedem
/// Start nur den nächsten Tippfehler meldet.
/// </summary>
public sealed record ScanConfigurationResolution(
    ResolvedScanConfiguration? Configuration,
    IReadOnlyList<string> Errors);

public static class ScanConfigurationResolver
{
    private const int MaximumSeconds = 86_400;

    /// <param name="defaultStateDirectory">
    /// Verzeichnis für Zustandsdateien, wenn die Konfiguration weder <c>stateDirectory</c> noch
    /// einen <c>statePath</c> je Ordner setzt.
    /// </param>
    public static ScanConfigurationResolution Resolve(
        ScanConfiguration? configuration,
        string defaultStateDirectory,
        StringComparer pathComparer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultStateDirectory);
        ArgumentNullException.ThrowIfNull(pathComparer);

        var errors = new List<string>();
        if (configuration is null)
        {
            errors.Add("The configuration file is empty.");
            return new(null, errors);
        }

        var interval = Seconds(configuration.IntervalSeconds, 900, 1, MaximumSeconds, "intervalSeconds", errors);
        var debounce = Seconds(configuration.DebounceSeconds, 10, 1, 3600, "debounceSeconds", errors);
        var stableFor = Seconds(configuration.StableForSeconds, 0, 0, MaximumSeconds, "stableForSeconds", errors);
        var timeout = Seconds(configuration.TimeoutSeconds, 0, 0, MaximumSeconds, "timeoutSeconds", errors);
        var parallelism = Range(
            configuration.Parallelism,
            Math.Max(1, Environment.ProcessorCount / 2),
            1,
            256,
            "parallelism",
            errors);

        var stateDirectory = string.IsNullOrWhiteSpace(configuration.StateDirectory)
            ? defaultStateDirectory
            : configuration.StateDirectory;

        var folders = new List<ResolvedScanFolder>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (configuration.Folders is null || configuration.Folders.Count == 0)
        {
            errors.Add("At least one entry in \"folders\" is required.");
        }
        else
        {
            for (var index = 0; index < configuration.Folders.Count; index++)
            {
                var folder = ResolveFolder(
                    configuration.Folders[index],
                    index,
                    configuration.Defaults,
                    stateDirectory,
                    names,
                    pathComparer,
                    errors);
                if (folder is not null)
                {
                    folders.Add(folder);
                }
            }
        }

        return errors.Count > 0
            ? new(null, errors)
            : new(
                new(
                    configuration.Mode ?? ScanMode.Interval,
                    TimeSpan.FromSeconds(interval),
                    TimeSpan.FromSeconds(debounce),
                    stableFor,
                    parallelism,
                    timeout,
                    Trimmed(configuration.LockPath),
                    Trimmed(configuration.LogPath),
                    configuration.History ?? false,
                    folders),
                []);
    }

    private static ResolvedScanFolder? ResolveFolder(
        ScanFolderConfiguration folder,
        int index,
        ScanCompressionSettings? defaults,
        string stateDirectory,
        HashSet<string> names,
        StringComparer pathComparer,
        List<string> errors)
    {
        var label = $"folders[{index.ToString(CultureInfo.InvariantCulture)}]";
        var name = Trimmed(folder.Name);
        if (name is null)
        {
            errors.Add($"{label}: \"name\" is required.");
        }
        else if (!name.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            // Der Name bildet den Vorgabe-Dateinamen des Zustands; ein Trenner oder ".." darf
            // daraus keinen anderen Pfad machen.
            errors.Add($"{label}: \"name\" may only contain letters, digits, '.', '_' and '-'.");
        }
        else if (!names.Add(name))
        {
            errors.Add($"{label}: \"name\" is not unique: {name}");
        }

        var input = Trimmed(folder.Input);
        var output = Trimmed(folder.Output);
        if (input is null)
        {
            errors.Add($"{label}: \"input\" is required.");
        }

        if (output is null)
        {
            errors.Add($"{label}: \"output\" is required.");
        }

        if (input is not null && output is not null)
        {
            input = Path.GetFullPath(input);
            output = Path.GetFullPath(output);
            if (pathComparer.Equals(input, output))
            {
                // Ein gemeinsamer Pfad hieße, die Originale zu ersetzen; das ist nach 7.2 eine
                // ausdrückliche Auswahl und nicht die Nebenwirkung zweier gleicher Pfade.
                errors.Add($"{label}: \"output\" must differ from \"input\".");
            }
        }

        var settings = ResolveSettings(defaults, folder.Settings, output, label, errors);
        if (name is null || input is null || output is null || settings is null)
        {
            return null;
        }

        var statePath = Trimmed(folder.StatePath) ?? Path.Combine(stateDirectory, $"{name}.json");
        return new(
            name,
            input,
            output,
            folder.Recursive ?? true,
            Path.GetFullPath(statePath),
            settings);
    }

    private static CompressionBatchSettings? ResolveSettings(
        ScanCompressionSettings? defaults,
        ScanCompressionSettings? overrides,
        string? outputDirectory,
        string label,
        List<string> errors)
    {
        var quality = Range(
            overrides?.Quality ?? defaults?.Quality, 80, 1, 100, $"{label} quality", errors);
        var progressiveLevel = Range(
            overrides?.ProgressiveLevel ?? defaults?.ProgressiveLevel, 2, 0, 2,
            $"{label} progressiveLevel", errors);
        var minimumSavings = Range(
            overrides?.MinSavingsPercent ?? defaults?.MinSavingsPercent, 0, 0, 99,
            $"{label} minSavingsPercent", errors);
        var alphaBackground = ResolveColor(
            overrides?.AlphaBackground ?? defaults?.AlphaBackground, label, errors);
        if (alphaBackground is null || outputDirectory is null)
        {
            return null;
        }

        return new(
            new JpegliSettings(
                quality,
                overrides?.ChromaSubsampling
                    ?? defaults?.ChromaSubsampling
                    ?? JpegliChromaSubsampling.Subsampling420,
                progressiveLevel),
            overrides?.Exif ?? defaults?.Exif ?? ExifPolicy.Remove,
            overrides?.ColorProfile ?? defaults?.ColorProfile ?? ColorProfilePolicy.Preserve,
            alphaBackground.Value,
            overrides?.Collision ?? defaults?.Collision ?? CollisionPolicy.Skip,
            overrides?.LargerOutput ?? defaults?.LargerOutput ?? LargerOutputPolicy.Discard,
            outputDirectory,
            overrides?.Suffix ?? defaults?.Suffix ?? "_compressed",
            minimumSavings);
    }

    private static RgbColor? ResolveColor(string? value, string label, List<string> errors)
    {
        if (value is null)
        {
            return RgbColor.White;
        }

        var text = value.Trim();
        if (text.Length != 7
            || text[0] != '#'
            || !byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            errors.Add($"{label} alphaBackground must be a hexadecimal color like \"#FFFFFF\": {value}");
            return null;
        }

        return new RgbColor(red, green, blue);
    }

    private static int Seconds(
        int? value, int fallback, int minimum, int maximum, string name, List<string> errors) =>
        Range(value, fallback, minimum, maximum, name, errors);

    private static int Range(
        int? value, int fallback, int minimum, int maximum, string name, List<string> errors)
    {
        if (value is null)
        {
            return fallback;
        }

        if (value < minimum || value > maximum)
        {
            errors.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"\"{name}\" must be between {minimum} and {maximum}: {value}"));
            return fallback;
        }

        return value.Value;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
