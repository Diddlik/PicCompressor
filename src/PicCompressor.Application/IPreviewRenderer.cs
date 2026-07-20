using PicCompressor.Domain;

namespace PicCompressor.Application;

/// <summary>
/// Speicherschonende Vorschau eines Bildes: aufrecht gedrehte, nach sRGB überführte Pixel als
/// dicht gepacktes RGB-Byte-Tripel je Pixel (Abschnitt 11).
/// </summary>
public sealed record PreviewImage(int Width, int Height, byte[] Rgb);

/// <summary>
/// Ergebnis einer Vorschauanforderung. Ein Fehler ist ein regulärer Produktzustand und wird als
/// Text gemeldet, nicht als Ausnahme.
/// </summary>
public sealed record PreviewResult(PreviewImage? Image, string? ErrorText)
{
    public static PreviewResult Failed(string errorText) => new(null, errorText);
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
}
