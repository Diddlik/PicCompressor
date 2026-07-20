using Avalonia;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Engine.Jpegli;
using PicCompressor.Gui;
using PicCompressor.Gui.Services;
using PicCompressor.Infrastructure;
using PicCompressor.NativeInterop;
using Velopack;

namespace PicCompressor.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().Run();
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var fileSystem = new PhysicalFileSystem(comparer);
        var inspector = new PhysicalInputImageInspector();
        var bridge = new NativeCodecBridge(TimeProvider.System);
        var jpegli = new JpegliEngineAdapter(bridge);
        var unavailableGuetzli = new UnavailableEngine(
            EngineIds.Guetzli,
            "Guetzli is not packaged for this runtime.");
        var executor = new CompressionExecutor(
            jpegli,
            new SafeOutputPublisher(fileSystem, inspector));
        var log = new JsonLinesDiagnosticLog(ApplicationDataPaths.DiagnosticLogPath);
        var settingsStore = new JsonApplicationSettingsStore(
            ApplicationDataPaths.SettingsFilePath,
            log);
        var settings = settingsStore.Load();
        var historyStore = new SqliteCompressionHistoryStore(
            ApplicationDataPaths.HistoryDatabasePath);

        // Aufbewahrung nach Abschnitt 13.1 mit der konfigurierten Dauer.
        try
        {
            historyStore
                .ApplyRetentionAsync(
                    DateTimeOffset.UtcNow - TimeSpan.FromDays(settings.HistoryRetentionDays),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException)
        {
            // Ein nicht lesbarer Verlauf darf den Start nicht verhindern.
        }

        App.ConfigureServices(
            new ApplicationCompressionService(
                new CompressionJobFactory(
                    fileSystem,
                    inspector,
                    new InputValidationLimits(500 * 1024 * 1024, 250_000_000),
                    TimeProvider.System),
                executor),
            new ApplicationEngineCatalogService(new EngineCatalog([jpegli, unavailableGuetzli])),
            new PersistentHistoryService(historyStore),
            settingsStore,
            bridge);

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }

    private sealed class UnavailableEngine(string engineId, string reason) : ICompressionEngine
    {
        public string EngineId { get; } = engineId;

        public Task<EngineCapability> DetectCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EngineCapability.Unavailable(EngineId, reason));

        public Task<EngineEncodingResult> EncodeAsync(
            CompressionJob job,
            string temporaryOutputPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                EngineEncodingResult.Failed(
                    CompressionErrorCategory.EngineUnavailable,
                    reason,
                    TimeSpan.Zero));
    }
}
