using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;
using PicCompressor.Gui.Views;

namespace PicCompressor.Gui.ViewTests;

/// <summary>
/// Prüft die Warteschlangenliste des Arbeitsbereichs auf einer laufenden Avalonia-Plattform:
/// die Virtualisierung sehr großer Stapel und die begrenzten Vorschaubilder
/// (Abschnitt 19.1, MP-002 Scheibe C).
/// </summary>
[Collection(AvaloniaCollection.Name)]
public sealed class QueueListTests(AvaloniaSession session)
{
    [Fact]
    public Task A_large_queue_realizes_only_a_few_rows() =>
        session.RunAsync(() =>
        {
            var dashboard = new DashboardViewModel(
                new SettingsViewModel(),
                new UnconfiguredCompressionService());
            for (var index = 0; index < 2000; index++)
            {
                dashboard.Queue.Add(
                    new QueueItemViewModel($"file{index}.png", EngineIds.Jpegli, 1000));
            }

            var view = new DashboardView { DataContext = dashboard };
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();

            // Ohne Virtualisierung entstünde je Datei eine Zeile; erzeugt werden darf nur, was
            // in die sichtbare Fläche passt (plus Reserve am Rand).
            var realized = view.GetVisualDescendants()
                .OfType<ItemsControl>()
                .Where(items => ReferenceEquals(items.ItemsSource, dashboard.Queue))
                .Sum(items => items.ItemsPanelRoot?.Children.Count ?? 0);

            Assert.InRange(realized, 1, 100);
        });

    [Fact]
    public Task A_row_reports_its_thumbnail_once_it_is_rendered() =>
        session.RunAsync(async () =>
        {
            var renderer = new CountingPreviewRenderer();
            var item = new QueueItemViewModel(
                "a.png",
                EngineIds.Jpegli,
                1000,
                new ThumbnailCache(renderer));

            var reported = false;
            item.PropertyChanged += (_, args) =>
                reported |= args.PropertyName == nameof(QueueItemViewModel.Thumbnail);

            // Der erste Zugriff fordert an und liefert noch nichts.
            Assert.Null(item.Thumbnail);
            await WaitUntil(() => reported);

            Assert.NotNull(item.Thumbnail);
            Assert.Equal(ThumbnailCache.MaxEdge, renderer.LastMaxEdge);

            // Ein zweiter Zugriff nimmt das gespeicherte Bild statt neu zu dekodieren.
            Assert.NotNull(item.Thumbnail);
            Assert.Equal(1, renderer.Calls);
        });

    [Fact]
    public Task The_cache_holds_at_most_its_capacity() =>
        session.RunAsync(async () =>
        {
            var cache = new ThumbnailCache(new CountingPreviewRenderer(), capacity: 2);
            foreach (var path in new[] { "a.png", "b.png", "c.png" })
            {
                await cache.RequestAsync(path, RgbColor.White);
            }

            Assert.Null(cache.Peek("a.png"));
            Assert.NotNull(cache.Peek("b.png"));
            Assert.NotNull(cache.Peek("c.png"));
        });

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class CountingPreviewRenderer : IPreviewRenderer
    {
        public int Calls { get; private set; }

        public int LastMaxEdge { get; private set; }

        public Task<PreviewResult> RenderPreviewAsync(
            string inputPath,
            int maxEdge,
            RgbColor alphaBackground,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastMaxEdge = maxEdge;
            return Task.FromResult(
                new PreviewResult(new PreviewImage(1, 1, [1, 2, 3], 10, 10), null));
        }

        public Task<EncodedPreviewResult> RenderEncodedPreviewAsync(
            string inputPath,
            int maxEdge,
            JpegliSettings settings,
            RgbColor alphaBackground,
            ExifPolicy exifPolicy,
            ColorProfilePolicy colorProfilePolicy,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
