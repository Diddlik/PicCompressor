using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;
using PicCompressor.Gui.Views;

namespace PicCompressor.Gui.ViewTests;

[Collection(AvaloniaCollection.Name)]
public sealed class MainWindowComparePreviewTests(AvaloniaSession session)
{
    [Fact]
    public Task Compare_preview_survives_MainWindow_navigation_and_hosting() =>
        session.RunAsync(async () =>
        {
            var main = new MainWindowViewModel(
                new UnconfiguredCompressionService(),
                new UnconfiguredEngineCatalogService(),
                new InMemoryHistoryService(),
                previewRenderer: new StubPreviewRenderer(
                    new PreviewImage(2, 1, [255, 0, 0, 0, 255, 0], 2, 1)));
            main.Dashboard.Queue.Add(Published("a.png", "a_compressed.jpg"));

            var window = new MainWindow { DataContext = main };
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();

            main.Dashboard.ShowCompareCommand!.Execute(null);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();
            await WaitUntil(() => main.Compare.HasPreview);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            window.UpdateLayout();

            var view = window.GetVisualDescendants().OfType<CompareView>().Single();
            var original = view.FindControl<Image>("OriginalImage")!;
            var compressed = view.FindControl<Image>("CompressedImage")!;

            Assert.Same(main.Compare, view.DataContext);
            Assert.IsType<WriteableBitmap>(original.Source);
            Assert.IsType<WriteableBitmap>(compressed.Source);
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

        public Task<EncodedPreviewResult> RenderEncodedPreviewAsync(
            string inputPath,
            int maxEdge,
            JpegliSettings settings,
            RgbColor alphaBackground,
            ExifPolicy exifPolicy,
            ColorProfilePolicy colorProfilePolicy,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EncodedPreviewResult(image, 0, null));
    }
}
