using System.Text.Json;
using PicCompressor.Application;

namespace PicCompressor.Infrastructure;

/// <summary>
/// Der Zustand wiederkehrender Scans als eine versionierte JSON-Datei (MP-005). Sie wird zuerst
/// temporär geschrieben und dann ersetzt, damit ein abgebrochener Lauf keinen halben Zustand
/// hinterlässt (Abschnitt 17.2).
/// </summary>
public sealed class JsonScanStateStore(string path)
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string path = Path.GetFullPath(
        !string.IsNullOrWhiteSpace(path)
            ? path
            : throw new ArgumentException("Path is required.", nameof(path)));

    private sealed record ScanStateDocument(int SchemaVersion, IReadOnlyList<ScanStateEntry> Entries);

    /// <summary>
    /// Liest den Zustand. Eine fehlende Datei ist ein leerer Zustand; eine unlesbare oder neuere
    /// Datei wird gemeldet, statt sie stillschweigend zu überschreiben.
    /// </summary>
    public IReadOnlyList<ScanStateEntry> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        ScanStateDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ScanStateDocument>(
                File.ReadAllText(path),
                SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Scan state file is not valid JSON: {path}", exception);
        }

        if (document is null)
        {
            return [];
        }

        if (document.SchemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Scan state schema version {document.SchemaVersion} is newer than the supported "
                + $"version {CurrentSchemaVersion}: {path}");
        }

        return document.Entries ?? [];
    }

    public void Save(IReadOnlyList<ScanStateEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    new ScanStateDocument(CurrentSchemaVersion, entries),
                    SerializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
