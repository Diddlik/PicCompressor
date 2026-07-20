using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;
using PicCompressor.Gui.Views;

namespace PicCompressor.Gui.ViewTests;

/// <summary>
/// Prüft die Vergleichsansicht auf einer laufenden Avalonia-Plattform: die Umwandlung der
/// Vorschaupixel, den Teiler, die gemeinsame Transformation beider Seiten und die
/// Tastaturbedienung (Abschnitt 11).
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class CompareViewTests(AvaloniaSession session)
{
    [Fact]
    public Task Preview_pixels_reach_the_bound_bitmap_in_the_right_order() =>
        session.RunAsync(async () =>
        {
            // Rot, Grün, Blau als dicht gepacktes RGB; die Ansicht braucht BGRA.
            var renderer = new StubPreviewRenderer(
                new PreviewImage(3, 1, [255, 0, 0, 0, 255, 0, 0, 0, 255]));
            var compare = new CompareViewModel(renderer);

            compare.Offer(Published("a.png", "a_compressed.jpg"));
            await WaitUntil(() => compare.HasPreview);

            var bitmap = Assert.IsType<WriteableBitmap>(compare.OriginalPreview);
            Assert.Equal(3, bitmap.PixelSize.Width);
            Assert.Equal(1, bitmap.PixelSize.Height);

            var pixels = ReadFirstRow(bitmap, 3);
            Assert.Equal<byte[]>([0, 0, 255, 255], pixels[0]);
            Assert.Equal<byte[]>([0, 255, 0, 255], pixels[1]);
            Assert.Equal<byte[]>([255, 0, 0, 255], pixels[2]);
        });

    [Fact]
    public Task A_new_selection_replaces_the_previous_preview() =>
        session.RunAsync(async () =>
        {
            var renderer = new StubPreviewRenderer(new PreviewImage(1, 1, [1, 2, 3]));
            var compare = new CompareViewModel(renderer);
            compare.Offer(Published("a.png", "a_compressed.jpg"));
            await WaitUntil(() => compare.HasPreview);
            var first = compare.OriginalPreview;

            compare.Selected = Published("b.png", "b_compressed.jpg");

            await WaitUntil(
                () => compare.HasPreview && !ReferenceEquals(first, compare.OriginalPreview));
        });

    [Fact]
    public Task Both_sides_share_one_transform_and_follow_zoom_and_pan() =>
        session.RunAsync(() =>
        {
            var compare = new CompareViewModel();
            Show(compare, out var view);

            var original = view.FindControl<Image>("OriginalImage")!;
            var compressed = view.FindControl<Image>("CompressedImage")!;
            // Ein gemeinsames Objekt kann nicht auseinanderlaufen.
            Assert.Same(compressed.RenderTransform, original.RenderTransform);

            compare.Zoom = 4;
            compare.Pan(30, -20);

            var group = Assert.IsType<TransformGroup>(original.RenderTransform);
            var scale = Assert.IsType<ScaleTransform>(group.Children[0]);
            var translate = Assert.IsType<TranslateTransform>(group.Children[1]);
            Assert.Equal(4, scale.ScaleX);
            Assert.Equal(4, scale.ScaleY);
            Assert.Equal(compare.PanX, translate.X);
            Assert.Equal(compare.PanY, translate.Y);
        });

    [Fact]
    public Task The_divider_clips_the_upper_layer_without_resizing_it() =>
        session.RunAsync(async () =>
        {
            // Ohne Auswahl ist die Vergleichsfläche ausgeblendet und wird nicht angeordnet;
            // ohne Vorschau haben die Bilder keine Größe.
            var compare = new CompareViewModel(
                new StubPreviewRenderer(new PreviewImage(4, 3, new byte[4 * 3 * 3])));
            compare.Offer(Published("a.png", "a_compressed.jpg"));
            await WaitUntil(() => compare.HasPreview);
            Show(compare, out var view);

            var surface = view.FindControl<Grid>("CompareSurface")!;
            var pane = view.FindControl<Panel>("OriginalPane")!;
            var original = view.FindControl<Image>("OriginalImage")!;
            var compressed = view.FindControl<Image>("CompressedImage")!;
            Assert.True(surface.Bounds.Width > 0);

            compare.DividerFraction = 0.25;

            // Beide Schichten liegen deckungsgleich; nur der Ausschnitt folgt dem Regler. Die
            // Bilder füllen die Fläche dabei nicht aus: Uniform passt sie auf ihr
            // Seitenverhältnis ein. Entscheidend ist, dass beide dieselbe Einpassung bekommen.
            Assert.Equal(compressed.Bounds, original.Bounds);
            Assert.True(original.Bounds.Width > 0);
            var clip = Assert.IsType<RectangleGeometry>(pane.Clip);
            Assert.Equal(surface.Bounds.Width * 0.25, clip.Rect.Width, 3);

            // Die Trennlinie sitzt mittig auf der Kante, damit sichtbar ist, wo geteilt wird.
            var line = view.FindControl<Border>("DividerLine")!;
            Assert.Equal(
                (surface.Bounds.Width * 0.25) - (line.Width / 2),
                line.Margin.Left,
                3);
        });

    [Fact]
    public Task Zoom_and_pan_are_reachable_from_the_keyboard() =>
        session.RunAsync(() =>
        {
            var compare = new CompareViewModel();
            compare.Offer(Published("a.png", "a_compressed.jpg"));
            var window = Show(compare, out var view);
            var surface = view.FindControl<Grid>("CompareSurface")!;
            // Die Fläche nimmt den Fokus wirklich an; die Tasten laufen danach über das Fenster.
            Assert.True(surface.Focus());

            Press(window, Key.Add, PhysicalKey.NumPadAdd);
            Assert.True(compare.Zoom > CompareViewModel.MinZoom);

            Press(window, Key.Right, PhysicalKey.ArrowRight);
            Assert.NotEqual(0, compare.PanX);

            Press(window, Key.D0, PhysicalKey.Digit0);
            Assert.Equal(CompareViewModel.MinZoom, compare.Zoom);
            Assert.Equal(0, compare.PanX);
        });

    /// <summary>
    /// Wartet begrenzt auf einen Zustand und lässt den Dispatcher dabei arbeiten. Die Vorschau
    /// wird asynchron geladen; ein fester Ablauf würde den Test an die aktuelle Implementierung
    /// binden.
    /// </summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static void Press(Window window, Key key, PhysicalKey physicalKey) =>
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);

    private static Window Show(CompareViewModel compare, out CompareView view)
    {
        view = new CompareView { DataContext = compare };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        // Erzwingt Messen und Anordnen, damit die Flächenmaße stehen.
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        window.UpdateLayout();
        return window;
    }

    private static byte[][] ReadFirstRow(WriteableBitmap bitmap, int width)
    {
        using var buffer = bitmap.Lock();
        var row = new byte[width * 4];
        Marshal.Copy(buffer.Address, row, 0, row.Length);
        return [.. Enumerable.Range(0, width).Select(x => row[(x * 4)..((x + 1) * 4)])];
    }

    private static QueueItemViewModel Published(string input, string output)
    {
        var item = new QueueItemViewModel(input, EngineIds.Jpegli, 100);
        item.ApplyOutcome(
            new CompressionOutcome(
                JobStatus.Succeeded, input, output, 100, 50, true, null, null, null));
        return item;
    }

    private sealed class StubPreviewRenderer(PreviewImage image) : IPreviewRenderer
    {
        public Task<PreviewResult> RenderPreviewAsync(
            string inputPath,
            int maxEdge,
            RgbColor alphaBackground,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PreviewResult(image, null));
    }
}
