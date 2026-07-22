namespace PicCompressor.Application;

/// <summary>
/// Enginespezifische Laufzeitgrenzen für das Encoding (Abschnitt 7.1, MP-004).
/// Fehlt für eine Engine ein Eintrag, gilt kein Zeitlimit. Die konkreten Werte
/// stammen aus dem Host (Desktop/CLI), weil sie Benutzereinstellungen sind; die
/// Application-Schicht bleibt so plattformneutral.
/// </summary>
public sealed class EngineRuntimeLimits
{
    private readonly IReadOnlyDictionary<string, TimeSpan> perEngine;

    /// <summary>Kein Zeitlimit für eine Engine.</summary>
    public static EngineRuntimeLimits None { get; } =
        new(new Dictionary<string, TimeSpan>(StringComparer.Ordinal));

    public EngineRuntimeLimits(IReadOnlyDictionary<string, TimeSpan> perEngine)
    {
        ArgumentNullException.ThrowIfNull(perEngine);

        var map = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        foreach (var (engineId, limit) in perEngine)
        {
            if (string.IsNullOrEmpty(engineId))
            {
                throw new ArgumentException("Engine id must not be empty.", nameof(perEngine));
            }

            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit.Ticks, nameof(perEngine));
            map[engineId] = limit;
        }

        this.perEngine = map;
    }

    /// <summary>
    /// Baut Grenzen aus Sekundenwerten je Engine. Ein Wert von <c>0</c> oder kleiner
    /// bedeutet „kein Limit“ und wird verworfen, sodass die Engine ohne Zeitlimit läuft.
    /// </summary>
    public static EngineRuntimeLimits FromSeconds(params (string EngineId, int Seconds)[] limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var map = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        foreach (var (engineId, seconds) in limits)
        {
            if (seconds > 0)
            {
                map[engineId] = TimeSpan.FromSeconds(seconds);
            }
        }

        return map.Count == 0 ? None : new EngineRuntimeLimits(map);
    }

    /// <summary>Das Zeitlimit der Engine oder <c>null</c>, wenn keines konfiguriert ist.</summary>
    public TimeSpan? For(string engineId) =>
        perEngine.TryGetValue(engineId, out var limit) ? limit : null;
}
