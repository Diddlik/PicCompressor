using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

public static class DesignData
{
    public static MainWindowViewModel MainWindow => CreateMainWindow();

    public static DashboardViewModel Dashboard => CreateMainWindow().Dashboard;

    public static SettingsViewModel Settings => CreateMainWindow().Settings;

    public static HistoryViewModel History => CreateHistory();

    public static CompareViewModel Compare => CreateCompare();

    private static MainWindowViewModel CreateMainWindow()
    {
        var main = new MainWindowViewModel(
            new UnconfiguredCompressionService(),
            new UnconfiguredEngineCatalogService(),
            CreateHistoryService(),
            new InMemoryApplicationSettingsStore(),
            new UnconfiguredInputDiscovery(),
            new DesignPreviewRenderer(),
            null,
            new UnconfiguredFileActionService());

        main.ApplyWidth(1200);
        main.Dashboard.Queue.Add(Published("sample-large-photo.jpg", "sample-large-photo_compressed.jpg", 4_850_000, 1_930_000));
        main.Dashboard.Queue.Add(Queued("portrait-to-preview.png", 2_400_000));
        return main;
    }

    private static CompareViewModel CreateCompare()
    {
        var compare = new CompareViewModel(new DesignPreviewRenderer(), new SettingsViewModel());
        compare.AttachQueue([Published("sample-large-photo.jpg", "sample-large-photo_compressed.jpg", 4_850_000, 1_930_000)]);
        return compare;
    }

    private static HistoryViewModel CreateHistory()
    {
        var history = new HistoryViewModel(CreateHistoryService());
        history.Entries.Add(new HistoryEntryViewModel(Record("holiday.jpg", 3_600_000, 1_500_000)));
        history.Entries.Add(new HistoryEntryViewModel(Record("product-shot.png", 1_700_000, 940_000)));
        return history;
    }

    private static IHistoryService CreateHistoryService()
    {
        var service = new InMemoryHistoryService();
        service.AppendAsync(Record("holiday.jpg", 3_600_000, 1_500_000), CancellationToken.None).GetAwaiter().GetResult();
        service.AppendAsync(Record("product-shot.png", 1_700_000, 940_000), CancellationToken.None).GetAwaiter().GetResult();
        return service;
    }

    private static HistoryRecord Record(string fileName, long inputSizeBytes, long outputSizeBytes) =>
        new(
            DateTimeOffset.Now.AddMinutes(-12),
            fileName,
            EngineIds.Jpegli,
            inputSizeBytes,
            outputSizeBytes,
            JobStatus.Succeeded,
            null);

    private static QueueItemViewModel Queued(string inputPath, long inputSizeBytes) =>
        new(inputPath, EngineIds.Jpegli, inputSizeBytes);

    private static QueueItemViewModel Published(
        string inputPath,
        string outputPath,
        long inputSizeBytes,
        long outputSizeBytes)
    {
        var item = new QueueItemViewModel(inputPath, EngineIds.Jpegli, inputSizeBytes);
        item.ApplyOutcome(
            new CompressionOutcome(
                JobStatus.Succeeded,
                inputPath,
                outputPath,
                inputSizeBytes,
                outputSizeBytes,
                true,
                null,
                null,
                null));
        return item;
    }

    private sealed class DesignPreviewRenderer : IPreviewRenderer
    {
        private static readonly PreviewImage Image = CreatePreviewImage();

        public Task<PreviewResult> RenderPreviewAsync(
            string inputPath,
            int maxEdge,
            RgbColor alphaBackground,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PreviewResult(Image, null));

        public Task<EncodedPreviewResult> RenderEncodedPreviewAsync(
            string inputPath,
            int maxEdge,
            JpegliSettings settings,
            RgbColor alphaBackground,
            ExifPolicy exifPolicy,
            ColorProfilePolicy colorProfilePolicy,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EncodedPreviewResult(Image, 1_930_000, null));

        private static PreviewImage CreatePreviewImage()
        {
            const int width = 320;
            const int height = 200;
            var rgb = new byte[width * height * 3];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = ((y * width) + x) * 3;
                    rgb[offset] = (byte)(90 + (x * 120 / width));
                    rgb[offset + 1] = (byte)(70 + (y * 150 / height));
                    rgb[offset + 2] = (byte)(180 - (x * 80 / width));
                }
            }

            return new PreviewImage(width, height, rgb, 1600, 1000);
        }
    }
}
