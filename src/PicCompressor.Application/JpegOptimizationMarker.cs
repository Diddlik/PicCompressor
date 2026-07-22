using System.Text;

namespace PicCompressor.Application;

/// <summary>
/// Provenienz-Marker „von PicCompressor optimiert“ (Issue #1). Der Marker liegt als
/// JPEG-Kommentarsegment (COM, <c>FF FE</c>) getrennt von EXIF und ICC; Dekoder ignorieren ihn,
/// die JPEG-Validierung bleibt gültig. Er ist unabhängig von der Metadatenrichtlinie und wird
/// auch bei „EXIF entfernen“ geschrieben (bewusste Produktentscheidung). Eine erneut eingereihte,
/// bereits markierte Datei wird nicht noch einmal komprimiert (Skip im Executor).
/// </summary>
public static class JpegOptimizationMarker
{
    /// <summary>Nutzlast des Kommentarsegments; ein stabiler, unübersetzter Bezeichner.</summary>
    public const string Token = "PicCompressor:optimized";

    private static readonly byte[] TokenBytes = Encoding.ASCII.GetBytes(Token);

    /// <summary>
    /// Fügt den Marker als COM-Segment direkt hinter dem SOI ein. Ist die Eingabe kein JPEG oder
    /// bereits markiert, wird sie unverändert (als Kopie) zurückgegeben. So kann der Aufrufer das
    /// Ergebnis bedenkenlos zurückschreiben.
    /// </summary>
    public static byte[] Embed(ReadOnlySpan<byte> jpeg)
    {
        if (!StartsWithSoi(jpeg) || IsMarked(jpeg))
        {
            return jpeg.ToArray();
        }

        var segmentLength = TokenBytes.Length + 2;
        if (segmentLength > 0xFFFF)
        {
            // Der Token ist kurz; diese Grenze kann praktisch nicht erreicht werden.
            return jpeg.ToArray();
        }

        var result = new byte[jpeg.Length + 4 + TokenBytes.Length];
        // SOI bleibt vorn; das COM-Segment folgt unmittelbar.
        result[0] = 0xFF;
        result[1] = 0xD8;
        result[2] = 0xFF;
        result[3] = 0xFE;
        result[4] = (byte)(segmentLength >> 8);
        result[5] = (byte)(segmentLength & 0xFF);
        TokenBytes.CopyTo(result, 6);
        jpeg[2..].CopyTo(result.AsSpan(6 + TokenBytes.Length));
        return result;
    }

    /// <summary>
    /// Meldet, ob ein Kommentarsegment vor dem Bilddatenstrom (SOS) den Token trägt. Läuft die
    /// Segmentkette bis SOS/EOI ab, ohne die Nutzdaten selbst zu durchsuchen.
    /// </summary>
    public static bool IsMarked(ReadOnlySpan<byte> jpeg)
    {
        if (!StartsWithSoi(jpeg))
        {
            return false;
        }

        var pos = 2;
        while (pos + 4 <= jpeg.Length)
        {
            if (jpeg[pos] != 0xFF)
            {
                return false;
            }

            // Auffüllbytes zwischen Segmenten überspringen.
            var marker = jpeg[pos + 1];
            while (marker == 0xFF && pos + 2 < jpeg.Length)
            {
                pos++;
                marker = jpeg[pos + 1];
            }

            // SOS und EOI beenden den Kopfbereich; danach folgen Bilddaten.
            if (marker is 0xDA or 0xD9)
            {
                return false;
            }

            // Marker ohne Längenfeld (Restart, TEM).
            if (marker is (>= 0xD0 and <= 0xD7) or 0x01)
            {
                pos += 2;
                continue;
            }

            var segmentLength = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
            if (segmentLength < 2 || pos + 2 + segmentLength > jpeg.Length)
            {
                return false;
            }

            if (marker == 0xFE)
            {
                var payload = jpeg.Slice(pos + 4, segmentLength - 2);
                if (payload.IndexOf(TokenBytes) >= 0)
                {
                    return true;
                }
            }

            pos += 2 + segmentLength;
        }

        return false;
    }

    /// <summary>Trägt die Nutzlast eines Kommentarsegments den Token? Für streamende Prüfer.</summary>
    public static bool PayloadContainsToken(ReadOnlySpan<byte> commentPayload) =>
        commentPayload.IndexOf(TokenBytes) >= 0;

    private static bool StartsWithSoi(ReadOnlySpan<byte> jpeg) =>
        jpeg.Length >= 2 && jpeg[0] == 0xFF && jpeg[1] == 0xD8;
}
