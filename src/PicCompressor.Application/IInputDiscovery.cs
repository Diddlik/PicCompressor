namespace PicCompressor.Application;

/// <summary>
/// Eine gefundene Eingabedatei. Größe und Änderungszeit stammen aus der Discovery, damit
/// Anzeige und wiederkehrender Scan (MP-005) kein zusätzliches Datei-I/O brauchen.
/// </summary>
public sealed record DiscoveredInput(
    string Path,
    string RelativeDirectory,
    long FileSizeBytes = 0,
    DateTimeOffset LastWriteTimeUtc = default);

public interface IInputDiscovery
{
    IReadOnlyList<DiscoveredInput> Discover(
        IEnumerable<string> inputPaths,
        bool recursive,
        string? excludedDirectory);

    /// <summary>
    /// Entdeckt Eingaben abseits des aufrufenden Threads (Abschnitt 7.3, MP-002). Der Lauf ist
    /// abbrechbar und meldet die bisher gefundene Anzahl über <paramref name="progress"/>, damit
    /// die Oberfläche bei großen Ordnern nicht blockiert.
    /// </summary>
    Task<IReadOnlyList<DiscoveredInput>> DiscoverAsync(
        IReadOnlyList<string> inputPaths,
        bool recursive,
        string? excludedDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}
