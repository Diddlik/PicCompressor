using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PicCompressor.Application;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;
using PicCompressor.Gui.Views;

namespace PicCompressor.Gui;

// Vollqualifiziert, weil PicCompressor.Application den Namen 'Application' verdeckt.
public sealed class App : Avalonia.Application
{
    private static Func<AppServices> serviceFactory = static () =>
        new(
            new UnconfiguredCompressionService(),
            new UnconfiguredEngineCatalogService(),
            new InMemoryHistoryService(),
            new InMemoryApplicationSettingsStore(),
            new UnconfiguredInputDiscovery(),
            new UnconfiguredFileActionService(),
            null,
            new UnconfiguredUpdateService(),
            new UnconfiguredClipboardImportService(),
            [],
            new UnconfiguredNotificationService());

    private readonly AppServices services = serviceFactory();

    public static void ConfigureServices(
        ICompressionService compressionService,
        IEngineCatalogService engineCatalogService,
        IHistoryService historyService,
        IApplicationSettingsStore settingsStore,
        IInputDiscovery inputDiscovery,
        IFileActionService fileActionService,
        IPreviewRenderer? previewRenderer = null,
        IUpdateService? updateService = null,
        IClipboardImportService? clipboardImport = null,
        IReadOnlyList<string>? initialInputs = null,
        INotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(compressionService);
        ArgumentNullException.ThrowIfNull(engineCatalogService);
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(inputDiscovery);
        ArgumentNullException.ThrowIfNull(fileActionService);
        serviceFactory = () =>
            new(
                compressionService,
                engineCatalogService,
                historyService,
                settingsStore,
                inputDiscovery,
                fileActionService,
                previewRenderer,
                updateService ?? new UnconfiguredUpdateService(),
                clipboardImport ?? new UnconfiguredClipboardImportService(),
                initialInputs ?? [],
                notifications ?? new UnconfiguredNotificationService());
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel(
                services.CompressionService,
                services.EngineCatalogService,
                services.HistoryService,
                services.SettingsStore,
                services.InputDiscovery,
                services.PreviewRenderer,
                services.UpdateService,
                services.FileActionService,
                services.ClipboardImport,
                services.InitialInputs,
                services.Notifications);

            var window = new MainWindow { DataContext = viewModel };
            window.Opened += async (_, _) =>
                await viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private sealed record AppServices(
        ICompressionService CompressionService,
        IEngineCatalogService EngineCatalogService,
        IHistoryService HistoryService,
        IApplicationSettingsStore SettingsStore,
        IInputDiscovery InputDiscovery,
        IFileActionService FileActionService,
        IPreviewRenderer? PreviewRenderer,
        IUpdateService UpdateService,
        IClipboardImportService ClipboardImport,
        IReadOnlyList<string> InitialInputs,
        INotificationService Notifications);
}
