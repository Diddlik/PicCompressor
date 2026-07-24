namespace PicCompressor.Gui.Services;

/// <summary>
/// Versionierter Deep-Link auf den Import-Anwendungsfall (MP-003). Ein Host registriert das Schema
/// <c>piccompressor://</c> und startet die Anwendung mit der URI als Argument. Die URI wird hier
/// rein (ohne Datei-I/O) geparst; die eigentliche Prüfung der Pfade nach Abschnitt 7.1 folgt beim
/// Einreihen. Die Version ist Teil des Kontrakts: eine unbekannte Version wird abgelehnt, statt
/// stillschweigend anders interpretiert zu werden.
///
/// Form: <c>piccompressor://v1/import?path=&lt;lokaler Pfad&gt;</c> (mehrere <c>path</c> erlaubt).
/// </summary>
public static class DeepLink
{
    public const string Scheme = "piccompressor";

    /// <summary>Aktuell unterstützte Schemaversionen; eine andere Version ist ein bewusster Fehler.</summary>
    private static readonly HashSet<string> SupportedVersions =
        new(StringComparer.Ordinal) { "v1" };

    /// <summary>
    /// Erkennt eine Deep-Link-URI und liefert die enthaltenen Importpfade. Gibt <c>false</c> für
    /// alles zurück, was kein wohlgeformter, unterstützter Import-Link ist; die Pfade werden nicht
    /// auf Existenz geprüft (das übernimmt der Aufnahmeweg).
    /// </summary>
    public static bool TryParseImport(string argument, out IReadOnlyList<string> paths)
    {
        paths = [];
        if (string.IsNullOrWhiteSpace(argument)
            || !Uri.TryCreate(argument, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Host trägt die Version, der erste Pfadabschnitt die Aktion.
        if (!SupportedVersions.Contains(uri.Host))
        {
            return false;
        }

        var action = uri.AbsolutePath.Trim('/');
        if (!string.Equals(action, "import", StringComparison.Ordinal))
        {
            return false;
        }

        var collected = ImportPathsFromQuery(uri.Query);
        if (collected.Count == 0)
        {
            return false;
        }

        paths = collected;
        return true;
    }

    /// <summary>Liest die <c>path</c>-Werte der Query, prozentdekodiert und ohne Leerwerte.</summary>
    private static List<string> ImportPathsFromQuery(string query)
    {
        var result = new List<string>();
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var key = pair[..separator];
            if (!string.Equals(key, "path", StringComparison.Ordinal))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
            }
        }

        return result;
    }
}
