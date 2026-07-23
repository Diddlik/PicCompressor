using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PicCompressor.Application;

namespace PicCompressor.Gui.ViewModels;

/// <summary>
/// Wandelt das dicht gepackte RGB des Wrappers in das von Avalonia erwartete BGRA um. Vergleich
/// und Warteschlangenliste teilen sich diesen Weg, damit Vorschaupixel überall gleich ankommen.
/// </summary>
internal static class PreviewBitmap
{
    public static Bitmap ToBitmap(PreviewImage image)
    {
        var rowBytes = image.Width * 4;
        var bgra = new byte[rowBytes * image.Height];
        for (var pixel = 0; pixel < image.Width * image.Height; ++pixel)
        {
            bgra[(pixel * 4) + 0] = image.Rgb[(pixel * 3) + 2];
            bgra[(pixel * 4) + 1] = image.Rgb[(pixel * 3) + 1];
            bgra[(pixel * 4) + 2] = image.Rgb[pixel * 3];
            bgra[(pixel * 4) + 3] = 255;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var buffer = bitmap.Lock();
        for (var y = 0; y < image.Height; ++y)
        {
            // Die Zeilen des Puffers dürfen breiter sein als die Bildzeile.
            Marshal.Copy(bgra, y * rowBytes, buffer.Address + (y * buffer.RowBytes), rowBytes);
        }

        return bitmap;
    }
}
