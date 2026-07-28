using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PicCompressor.Application;

/// <summary>
/// Eine in einem früheren Scan verarbeitete Eingabe (MP-005). <see cref="OutputPath"/> ist leer,
/// wenn der Job erfolgreich ohne Ausgabe endete (bereits markierte Eingabe, verworfene Ausgabe);
/// dann hängt die Wiedererkennung allein an Eingabe und Einstellungen.
/// </summary>
public sealed record ScanStateEntry(
    string InputPath,
    long InputSizeBytes,
    long InputModifiedUtcTicks,
    string SettingsFingerprint,
    string OutputPath);

/// <summary>
/// Aufteilung der gefundenen Eingaben eines wiederkehrenden Scans in zu verarbeitende,
/// unveränderte und noch nicht stabile Dateien.
/// </summary>
public sealed record ScanSelection(
    IReadOnlyList<DiscoveredInput> Inputs,
    IReadOnlyList<ScanStateEntry> Unchanged,
    int UnstableCount);

/// <summary>
/// Regeln des idempotenten Scans (MP-005): eine unveränderte Eingabe mit gültiger Ausgabe ist ein
/// erfolgreicher No-op, eine noch wachsende Datei wird erst in einem späteren Lauf verarbeitet.
/// </summary>
public static class IncrementalScan
{
    /// <summary>
    /// Verbindet alle Einstellungen, die das Ergebnis bestimmen, zu einem stabilen Fingerabdruck.
    /// Eine Änderung daran erzeugt beim nächsten Lauf genau einen neuen Job.
    /// </summary>
    public static string Fingerprint(CompressionBatchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{settings.EngineSettings.Fingerprint};exif={settings.ExifPolicy};color={settings.ColorProfilePolicy};alpha={settings.AlphaBackground};collision={settings.CollisionPolicy};larger={settings.LargerOutputPolicy};outputDirectory={settings.OutputDirectory};suffix={settings.Suffix};minSavings={settings.MinimumSavingsPercent}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public static ScanSelection Select(
        IReadOnlyList<DiscoveredInput> inputs,
        IReadOnlyList<ScanStateEntry> state,
        string fingerprint,
        TimeSpan minimumStableAge,
        DateTimeOffset now,
        IFileSystem fileSystem,
        StringComparer pathComparer)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathComparer);

        var recorded = ToLookup(state, pathComparer);
        var selected = new List<DiscoveredInput>(inputs.Count);
        var unchanged = new List<ScanStateEntry>();
        var unstable = 0;

        foreach (var input in inputs)
        {
            // Eine Datei, die noch geschrieben oder synchronisiert wird, wird nicht verarbeitet;
            // sie bleibt unangetastet und wird im nächsten Lauf erneut geprüft.
            if (minimumStableAge > TimeSpan.Zero && now - input.LastWriteTimeUtc < minimumStableAge)
            {
                unstable++;
                continue;
            }

            if (recorded.TryGetValue(input.Path, out var entry)
                && entry.InputSizeBytes == input.FileSizeBytes
                && entry.InputModifiedUtcTicks == input.LastWriteTimeUtc.UtcTicks
                && string.Equals(entry.SettingsFingerprint, fingerprint, StringComparison.Ordinal)
                && (entry.OutputPath.Length == 0 || fileSystem.FileExists(entry.OutputPath)))
            {
                unchanged.Add(entry);
                continue;
            }

            selected.Add(input);
        }

        return new(selected, unchanged, unstable);
    }

    /// <summary>
    /// Bildet den neuen Zustand: die Einträge dieses Laufs plus jene früheren Einträge, die
    /// nicht Teil dieses Scans waren und deren Eingabe es noch gibt. Gelöschte Eingaben fallen
    /// heraus, damit der Zustand nicht unbegrenzt wächst.
    /// </summary>
    public static IReadOnlyList<ScanStateEntry> Merge(
        IReadOnlyList<ScanStateEntry> previous,
        IReadOnlyList<DiscoveredInput> scanned,
        IReadOnlyList<ScanStateEntry> current,
        IFileSystem fileSystem,
        StringComparer pathComparer)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(scanned);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathComparer);

        var merged = ToLookup(current, pathComparer);
        var scannedPaths = new HashSet<string>(scanned.Select(input => input.Path), pathComparer);
        foreach (var entry in previous)
        {
            if (!scannedPaths.Contains(entry.InputPath)
                && !merged.ContainsKey(entry.InputPath)
                && fileSystem.FileExists(entry.InputPath))
            {
                merged.Add(entry.InputPath, entry);
            }
        }

        return merged.Values.OrderBy(entry => entry.InputPath, pathComparer).ToArray();
    }

    private static Dictionary<string, ScanStateEntry> ToLookup(
        IReadOnlyList<ScanStateEntry> entries,
        StringComparer pathComparer)
    {
        var lookup = new Dictionary<string, ScanStateEntry>(entries.Count, pathComparer);
        foreach (var entry in entries)
        {
            lookup[entry.InputPath] = entry;
        }

        return lookup;
    }
}
