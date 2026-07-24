using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;
using PicCompressor.Gui.Views;

namespace PicCompressor.Gui.ViewTests;

/// <summary>
/// Prüft den Vergleich am tatsächlich gezeichneten Bild. Ob beide Seiten wirklich übereinander
/// liegen, lässt sich an Layouteigenschaften nicht ablesen: eine schmalere obere Fläche passt
/// das Bild darin neu ein, obwohl die gesetzte Breite unverändert aussieht.
///
/// Das Original ist weiß, das Ergebnis schwarz. Die Unterscheidung über die Helligkeit ist
/// unabhängig von der Kanalreihenfolge des kopflosen Bildpuffers.
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class CompareWipeTests(AvaloniaSession session)
{
    private const int WindowWidth = 800;
    private const int WindowHeight = 700;

    [Fact]
    public Task Both_layers_cover_the_same_area_regardless_of_the_divider() =>
        session.RunAsync(async () =>
        {
            // Ganz links: nur das Ergebnis. Ganz rechts: nur das Original.
            var (onlyCompressed, surface) = await RenderAsync(0.0);
            var (onlyOriginal, _) = await RenderAsync(1.0);

            var compressedSpan = ContentSpan(onlyCompressed, surface);
            var originalSpan = ContentSpan(onlyOriginal, surface);

            Assert.NotEqual(0, compressedSpan.Length);
            // Gleiche Ausdehnung heißt: gleiche Skalierung und gleiche Lage — eine Schicht über
            // der anderen statt zwei Bilder nebeneinander.
            Assert.Equal(compressedSpan, originalSpan);
        });

    [Fact]
    public Task The_divider_splits_the_same_image_into_two_layers() =>
        session.RunAsync(async () =>
        {
            var (row, surface) = await RenderAsync(0.5);
            var span = ContentSpan(row, surface);
            var divider = (int)(surface.X + (surface.Width / 2));

            // Links vom Regler das Original, rechts davon das Ergebnis — an derselben Stelle.
            Assert.True(span.Start < divider && span.End > divider, "the content crosses the divider");
            Assert.True(IsBright(row, (span.Start + divider) / 2), "left of the divider is the original");
            Assert.False(IsBright(row, (divider + span.End) / 2), "right of the divider is the result");
        });

    [Fact]
    public Task The_divider_line_is_drawn_on_the_edge() =>
        session.RunAsync(async () =>
        {
            // Oberhalb des mittigen Lime-Griffs gemessen: dort liegt die reine Teilerlinie.
            var (row, surface) = await RenderAsync(0.5, -60);
            var divider = (int)(surface.X + (surface.Width / 2));

            // Direkt auf der Kante liegt die Linie, kurz daneben schon das schwarze Ergebnis.
            Assert.True(IsBright(row, divider), "the divider line covers the edge");
            Assert.False(IsBright(row, divider + 20), "the result stays dark beside the line");
        });

    /// <summary>
    /// Waagerechte Ausdehnung des Bildinhalts innerhalb der Vergleichsfläche. Der Fensterrand
    /// bleibt außen vor: dort liegt reines Weiß beziehungsweise Schwarz, das sonst als Inhalt
    /// gezählt würde.
    /// </summary>
    private static (int Start, int End, int Length) ContentSpan(byte[] row, Rect surface)
    {
        var start = -1;
        var end = -1;
        for (var x = (int)surface.X; x < (int)surface.Right; x++)
        {
            if (!IsContent(row, x))
            {
                continue;
            }

            start = start < 0 ? x : start;
            end = x;
        }

        return (start, end, start < 0 ? 0 : end - start);
    }

    /// <summary>
    /// Bildinhalt ist genau reines Weiß oder reines Schwarz. Ein Schwellwert genügt nicht: der
    /// helle Fensterhintergrund liegt nah am Weiß der Originalvorschau und würde mitgezählt.
    /// </summary>
    private static bool IsContent(byte[] row, int x)
    {
        var (r, g, b) = Pixel(row, x);
        return (r == 255 && g == 255 && b == 255) || (r == 0 && g == 0 && b == 0);
    }

    private static bool IsBright(byte[] row, int x) => Pixel(row, x).R > 230;

    private static (byte R, byte G, byte B) Pixel(byte[] row, int x) =>
        (row[x * 4], row[(x * 4) + 1], row[(x * 4) + 2]);

    /// <summary>Rendert die Ansicht und gibt eine Bildzeile durch die Mitte der Vergleichsfläche zurück.</summary>
    private static async Task<(byte[] Row, Rect Surface)> RenderAsync(double dividerFraction, int rowOffset = 0)
    {
        var compare = new CompareViewModel(new MonochromeRenderer());
        compare.AttachQueue(
            new ObservableCollection<QueueItemViewModel>(
                [Published("a.png", "a_compressed.jpg")]));
        for (var attempt = 0; attempt < 100 && !compare.HasPreview; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(compare.HasPreview);
        compare.DividerFraction = dividerFraction;

        var view = new CompareView { DataContext = compare };
        var window = new Window { Content = view, Width = WindowWidth, Height = WindowHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        var surface = view.FindControl<Grid>("CompareSurface")!;
        // Bounds sind elternrelativ; gebraucht wird die Lage im Fenster, also im Bildpuffer.
        var rect = new Rect(surface.TranslatePoint(default, window)!.Value, surface.Bounds.Size);

        // Gemessen wird durch die Mitte des gezeichneten Bildes, nicht der Fläche: das Bild ist
        // auf sein Seitenverhältnis eingepasst und füllt die Fläche nicht.
        var image = view.FindControl<Image>("CompressedImage")!;
        var imageRect = new Rect(
            image.TranslatePoint(default, window)!.Value,
            image.Bounds.Size);
        return (ReadRow(window.CaptureRenderedFrame()!, (int)imageRect.Center.Y + rowOffset), rect);
    }

    private static byte[] ReadRow(Bitmap frame, int y)
    {
        var stride = frame.PixelSize.Width * 4;
        var buffer = new byte[stride * frame.PixelSize.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(
                new PixelRect(frame.PixelSize),
                handle.AddrOfPinnedObject(),
                buffer.Length,
                stride);
        }
        finally
        {
            handle.Free();
        }

        return buffer[(y * stride)..((y + 1) * stride)];
    }

    private static QueueItemViewModel Published(string input, string output)
    {
        var item = new QueueItemViewModel(input, EngineIds.Jpegli, 100);
        item.ApplyOutcome(
            new CompressionOutcome(
                JobStatus.Succeeded, input, output, 100, 50, true, null, null, null));
        return item;
    }

    /// <summary>Original weiß, Ergebnis schwarz; 4:3, damit eine falsche Einpassung auffällt.</summary>
    private sealed class MonochromeRenderer : IPreviewRenderer
    {
        public Task<PreviewResult> RenderPreviewAsync(
            string inputPath,
            int maxEdge,
            RgbColor alphaBackground,
            CancellationToken cancellationToken)
        {
            var value = (byte)(inputPath.EndsWith(".png", StringComparison.Ordinal) ? 255 : 0);
            var pixels = new byte[40 * 30 * 3];
            Array.Fill(pixels, value);
            return Task.FromResult(new PreviewResult(new PreviewImage(40, 30, pixels, 40, 30), null));
        }

        public Task<EncodedPreviewResult> RenderEncodedPreviewAsync(
            string inputPath,
            int maxEdge,
            JpegliSettings settings,
            RgbColor alphaBackground,
            ExifPolicy exifPolicy,
            ColorProfilePolicy colorProfilePolicy,
            CancellationToken cancellationToken) =>
            Task.FromResult(RenderPreviewAsync(inputPath, maxEdge, alphaBackground, cancellationToken).Result is var r && r.Image is not null ? new EncodedPreviewResult(r.Image, 0, null) : EncodedPreviewResult.Failed("x"));
    }
}
