using Avalonia.Media.Imaging;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Services;

/// <summary>
/// Begrenzter Vorschaubildspeicher der Warteschlangenliste (MP-002 Scheibe C). Die Liste
/// dekodiert nie ein vollständiges Bild in den Speicher: die Vorschau entsteht über denselben
/// nativen Pfad wie der Vergleich, aber mit einer harten Kantenbegrenzung
/// (<see cref="MaxEdge"/>). Gleichzeitig laufen höchstens <see cref="MaxConcurrentDecodes"/>
/// Dekodierungen, und es werden höchstens <c>capacity</c> Bilder gehalten — beides bleibt damit
/// unabhängig von der Größe des Stapels beschränkt.
/// </summary>
public sealed class ThumbnailCache
{
    /// <summary>Obergrenze der längeren Kante eines Vorschaubildes in Bildpunkten.</summary>
    public const int MaxEdge = 48;

    /// <summary>Damit schnelles Scrollen keine Dekodierwelle auslöst.</summary>
    public const int MaxConcurrentDecodes = 2;

    private readonly Lock gate = new();

    /// <summary>
    /// Ein Eintrag steht ab der Anforderung, damit dieselbe Datei nicht mehrfach dekodiert wird;
    /// der fertige Wert steckt in der abgeschlossenen Aufgabe.
    /// </summary>
    private readonly Dictionary<string, Task<Bitmap?>> entries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Queue<string> order = new();
    private readonly SemaphoreSlim decodeSlots = new(MaxConcurrentDecodes);
    private readonly IPreviewRenderer renderer;
    private readonly int capacity;

    public ThumbnailCache(IPreviewRenderer renderer, int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.renderer = renderer;
        this.capacity = capacity;
    }

    /// <summary>Bereits geladenes Bild oder <c>null</c>; löst selbst nichts aus.</summary>
    public Bitmap? Peek(string path)
    {
        lock (gate)
        {
            return entries.TryGetValue(path, out var entry) && entry.IsCompletedSuccessfully
                ? entry.Result
                : null;
        }
    }

    /// <summary>
    /// Fordert das Bild an. Eine laufende oder abgeschlossene Anforderung derselben Datei wird
    /// wiederverwendet; <c>null</c> heißt „keine Vorschau“ und ist ein regulärer Zustand.
    /// </summary>
    public Task<Bitmap?> RequestAsync(string path, RgbColor alphaBackground)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        lock (gate)
        {
            if (entries.TryGetValue(path, out var existing))
            {
                return existing;
            }

            var pending = RenderAsync(path, alphaBackground);
            entries[path] = pending;
            order.Enqueue(path);

            // Ältester Eintrag zuerst. Die Kapazität liegt weit über der Zahl gleichzeitig
            // sichtbarer Zeilen, deshalb genügt diese Reihenfolge ohne Nutzungsverfolgung.
            while (order.Count > capacity)
            {
                entries.Remove(order.Dequeue());
            }

            return pending;
        }
    }

    private async Task<Bitmap?> RenderAsync(string path, RgbColor alphaBackground)
    {
        await decodeSlots.WaitAsync().ConfigureAwait(true);
        try
        {
            var result = await renderer
                .RenderPreviewAsync(path, MaxEdge, alphaBackground, CancellationToken.None)
                .ConfigureAwait(true);

            return result.Image is PreviewImage image ? PreviewBitmap.ToBitmap(image) : null;
        }
        catch (Exception)
        {
            // Ein Vorschaubild ist schmückend: keine Datei, kein Ergebnis und kein Job hängt
            // daran. Die Zeile bleibt ohne Bild, statt den Arbeitsbereich zu stören.
            return null;
        }
        finally
        {
            decodeSlots.Release();
        }
    }
}
