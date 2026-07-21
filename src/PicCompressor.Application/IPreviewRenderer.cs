using PicCompressor.Domain;

namespace PicCompressor.Application;

/// <summary>
/// Vorschau eines Bildes: aufrecht gedrehte, nach sRGB überführte Pixel als dicht gepacktes
/// RGB-Byte-Tripel je Pixel (Abschnitt 11).
/// </summary>
/// <param name="SourceWidth">Breite des aufrecht gedrehten Originals vor dem Verkleinern.</param>
/// <param name="SourceHeight">Höhe des aufrecht gedrehten Originals vor dem Verkleinern.</param>
public sealed record PreviewImage(
    int Width,
    int Height,
    byte[] Rgb,
    int SourceWidth,
    int SourceHeight)
{
    /// <summary>
    /// Verhältnis der Vorschau zum Original. Unter <c>1</c> wurde verkleinert; die Ansicht kann
    /// dann keine echte 1:1-Darstellung zeigen.
    /// </summary>
    public double ScaleFromSource => SourceWidth == 0 ? 1 : (double)Width / SourceWidth;
}

/// <summary>
/// Ergebnis einer Vorschauanforderung. Ein Fehler ist ein regulärer Produktzustand und wird als
/// Text gemeldet, nicht als Ausnahme.
/// </summary>
public sealed record PreviewResult(PreviewImage? Image, string? ErrorText)
{
    public static PreviewResult Failed(string errorText) => new(null, errorText);
}

/// <summary>
/// Ergebnis einer Probekompression: Vorschau des Ergebnisses und dessen Größe in Bytes, ohne
/// dass eine Datei entstanden ist.
/// </summary>
public sealed record EncodedPreviewResult(
    PreviewImage? Image,
    long EncodedSizeBytes,
    string? ErrorText)
{
    public static EncodedPreviewResult Failed(string errorText) => new(null, 0, errorText);
}

/// <summary>
/// Erzeugt Vorschauen über dieselbe native Dekodierung wie das Encoding, damit Orientierung und
/// Farbprofil identisch behandelt werden.
/// </summary>
public interface IPreviewRenderer
{
    /// <param name="maxEdge">Obergrenze der längeren Kante in Pixeln; größere Bilder werden verkleinert.</param>
    Task<PreviewResult> RenderPreviewAsync(
        string inputPath,
        int maxEdge,
        RgbColor alphaBackground,
        CancellationToken cancellationToken);

    /// <summary>
    /// Komprimiert die Eingabe mit den angegebenen Einstellungen ausschließlich im Speicher und
    /// gibt die Vorschau des Ergebnisses zurück. Damit lässt sich ein Bild beurteilen, bevor
    /// eine Datei geschrieben wird; veröffentlicht wird dabei nichts (Abschnitt 7.2).
    /// </summary>
    Task<EncodedPreviewResult> RenderEncodedPreviewAsync(
        string inputPath,
        int maxEdge,
        JpegliSettings settings,
        RgbColor alphaBackground,
        ExifPolicy exifPolicy,
        ColorProfilePolicy colorProfilePolicy,
        CancellationToken cancellationToken);
}
