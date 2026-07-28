using System.Text.Json;
using System.Text.Json.Serialization;
using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Infrastructure;

/// <summary>
/// Liest die Konfigurationsdatei des Dauerbetriebs (D-055). Sie wird strikt gelesen: ein unbekanntes
/// Feld, ein ungültiger Wert oder eine neuere Schemaversion ist ein Fehler statt eines stillen
/// Vorgabewerts, weil ein unbeaufsichtigter Lauf sonst dauerhaft anders arbeitet als beabsichtigt.
/// </summary>
public sealed class JsonScanConfigurationStore(string path)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // Der eigene Konverter steht zuerst: die Auswahl folgt der Reihenfolge, und die
        // Enum-Fabrik würde sonst jeden Aufzählungstyp beanspruchen.
        Converters =
        {
            new ChromaSubsamplingConverter(),
            new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)
        }
    };

    private readonly string path = Path.GetFullPath(
        !string.IsNullOrWhiteSpace(path)
            ? path
            : throw new ArgumentException("Path is required.", nameof(path)));

    public ScanConfiguration Load()
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Configuration file does not exist.", path);
        }

        ScanConfiguration? configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<ScanConfiguration>(
                File.ReadAllText(path),
                SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Configuration file is not valid: {path}: {exception.Message}",
                exception);
        }

        if (configuration is null)
        {
            throw new InvalidDataException($"Configuration file is empty: {path}");
        }

        if (configuration.SchemaVersion != ScanConfiguration.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Configuration schema version {configuration.SchemaVersion} is not supported; "
                + $"expected {ScanConfiguration.CurrentSchemaVersion}: {path}");
        }

        return configuration;
    }

    /// <summary>
    /// Nimmt neben den Enum-Namen auch die in der Bildbearbeitung üblichen Schreibweisen
    /// <c>444</c>, <c>440</c>, <c>422</c> und <c>420</c> an; eine von Hand gepflegte
    /// Konfigurationsdatei soll nicht <c>Subsampling420</c> verlangen.
    /// </summary>
    private sealed class ChromaSubsamplingConverter : JsonConverter<JpegliChromaSubsampling>
    {
        public override JpegliChromaSubsampling Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString() ?? string.Empty;
            return text switch
            {
                "444" or "4:4:4" => JpegliChromaSubsampling.Subsampling444,
                "440" or "4:4:0" => JpegliChromaSubsampling.Subsampling440,
                "422" or "4:2:2" => JpegliChromaSubsampling.Subsampling422,
                "420" or "4:2:0" => JpegliChromaSubsampling.Subsampling420,
                _ => Enum.TryParse<JpegliChromaSubsampling>(text, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed)
                        ? parsed
                        : throw new JsonException(
                            $"chromaSubsampling must be 444, 440, 422 or 420: {text}")
            };
        }

        public override void Write(
            Utf8JsonWriter writer, JpegliChromaSubsampling value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
