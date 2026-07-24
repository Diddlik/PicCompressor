using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using PicCompressor.Gui.Services;
using PicCompressor.Infrastructure;

namespace PicCompressor.Desktop;

/// <summary>
/// Host-Adapter für das Einreihen aus der Zwischenablage (MP-003). Eine Dateiliste wird direkt
/// als Pfade gemeldet; Bilddaten ohne Datei werden zuerst als verwaltete temporäre Eingabe
/// abgelegt (<see cref="TemporaryInputStore"/>) und danach wie jede andere Datei geprüft. Eine
/// leere oder unpassende Zwischenablage ist kein Fehler, sondern eine leere Liste.
/// </summary>
public sealed class DesktopClipboardImportService(TemporaryInputStore temporaryInputs)
    : IClipboardImportService
{
    public async Task<IReadOnlyList<string>> ReadImportPathsAsync(
        CancellationToken cancellationToken)
    {
        var clipboard = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
        if (clipboard is null)
        {
            return [];
        }

        try
        {
            var files = await clipboard.TryGetFilesAsync().ConfigureAwait(true);
            var paths = files?
                .Select(file => file.TryGetLocalPath())
                .OfType<string>()
                .ToList();
            if (paths is { Count: > 0 })
            {
                return paths;
            }

            var bitmap = await clipboard.TryGetBitmapAsync().ConfigureAwait(true);
            if (bitmap is null)
            {
                return [];
            }

            using (bitmap)
            {
                // Avalonia schreibt PNG; verlustfrei ist hier Pflicht, weil das Ergebnis die
                // Eingabe der Kompression ist.
                using var buffer = new MemoryStream();
                bitmap.Save(buffer, PngBitmapEncoderOptions.Default);
                var path = await temporaryInputs
                    .SaveAsync(buffer.ToArray(), ".png", cancellationToken)
                    .ConfigureAwait(true);
                return [path];
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException
                or PlatformNotSupportedException)
        {
            // Eine unlesbare Zwischenablage ist kein Produktfehler; es wird nichts eingereiht.
            return [];
        }
    }
}
