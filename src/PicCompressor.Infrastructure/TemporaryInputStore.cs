namespace PicCompressor.Infrastructure;

/// <summary>
/// Verwaltete temporäre Eingaben (MP-003): Bilddaten ohne Datei — etwa aus der Zwischenablage —
/// werden hier zuerst sicher abgelegt und erst danach regulär geprüft und eingereiht.
///
/// Die Dateien liegen im Anwendungsdatenverzeichnis, nicht im gemeinsam beschreibbaren
/// Systemtemp und nicht im Bild-Zielordner (Abschnitt 13.3, 16). Geschrieben wird zuerst unter
/// einem Reservierungsnamen und erst nach vollständigem Schreiben umbenannt, damit kein halb
/// geschriebener Inhalt als Eingabe erscheint (Abschnitt 17.2).
/// </summary>
public sealed class TemporaryInputStore
{
    private readonly string directory;

    /// <param name="directory">Ablageort; ohne Angabe das Anwendungsdatenverzeichnis.</param>
    public TemporaryInputStore(string? directory = null)
    {
        this.directory = directory
            ?? Path.Combine(ApplicationDataPaths.ApplicationDataDirectory, "clipboard");
    }

    /// <summary>
    /// Entfernt die Ablagen früherer Läufe. Wird beim Start aufgerufen, solange noch keine
    /// Eingabe dieses Laufs eingereiht ist; ein nicht löschbarer Rest ist kein Produktfehler.
    /// </summary>
    public void ClearPreviousRuns()
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Eine belegte Altdatei darf den Start nicht verhindern.
        }
    }

    /// <summary>Legt den Inhalt ab und gibt den Pfad der fertigen Datei zurück.</summary>
    public async Task<string> SaveAsync(
        ReadOnlyMemory<byte> content,
        string extension,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():n}{extension}");
        var reserved = path + ".partial";

        await using (var stream = new FileStream(
            reserved,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        {
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        }

        File.Move(reserved, path);
        return path;
    }
}
