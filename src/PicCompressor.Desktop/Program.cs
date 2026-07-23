using Avalonia;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Engine.Guetzli;
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
        var guetzli = new GuetzliEngineAdapter(bridge);
        // Vorablesen der Rotationsgrenzen: der Log braucht sie schon bei der Konstruktion,
        // bevor der Speicher mit ihm Korrekturen melden kann. Diese Sondierung meldet nichts.
        var logPreferences = new JsonApplicationSettingsStore(
            ApplicationDataPaths.SettingsFilePath,
            NullDiagnosticLog.Instance).Load();
        var log = new JsonLinesDiagnosticLog(
            ApplicationDataPaths.DiagnosticLogPath,
            (long)logPreferences.LogMaxFileMegabytes * 1024 * 1024,
            logPreferences.LogRetainedFiles);
        var settingsStore = new JsonApplicationSettingsStore(
            ApplicationDataPaths.SettingsFilePath,
            log);
        var settings = settingsStore.Load();
        // Enginespezifisches Zeitlimit (MP-004, Abschnitt 7.1); 0 = kein Limit.
        var executor = new CompressionExecutor(
            [jpegli, guetzli],
            new SafeOutputPublisher(fileSystem, inspector),
            TimeProvider.System,
            EngineRuntimeLimits.FromSeconds(
                (JpegliSettings.JpegliEngineId, settings.JpegliTimeoutSeconds),
                (GuetzliSettings.GuetzliEngineId, settings.GuetzliTimeoutSeconds)));
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

        // Prereleases werden mitgezogen, solange der direkte Kanal in der Alpha-Phase ist;
        // stabile Releases erhalten dieselbe Quelle ohne Prerelease-Flag.
        // Reste früherer Läufe entfernen, solange noch keine Eingabe dieses Laufs eingereiht ist.
        var temporaryInputs = new TemporaryInputStore();
        temporaryInputs.ClearPreviousRuns();

        var updateService = new VelopackUpdateService(
            "https://github.com/Diddlik/PicCompressor",
            includePrereleases: true);

        App.ConfigureServices(
            new ApplicationCompressionService(
                new CompressionJobFactory(
                    fileSystem,
                    inspector,
                    new InputValidationLimits(500 * 1024 * 1024, 250_000_000),
                    TimeProvider.System),
                executor),
            new ApplicationEngineCatalogService(new EngineCatalog([jpegli, guetzli])),
            new PersistentHistoryService(historyStore),
            settingsStore,
            new PhysicalInputDiscovery(comparer),
            new DesktopFileActionService(),
            bridge,
            updateService,
            new DesktopClipboardImportService(temporaryInputs));

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
