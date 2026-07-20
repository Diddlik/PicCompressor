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
            null);

    private readonly AppServices services = serviceFactory();

    public static void ConfigureServices(
        ICompressionService compressionService,
        IEngineCatalogService engineCatalogService,
        IHistoryService historyService,
        IApplicationSettingsStore settingsStore,
        IPreviewRenderer? previewRenderer = null)
    {
        ArgumentNullException.ThrowIfNull(compressionService);
        ArgumentNullException.ThrowIfNull(engineCatalogService);
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(settingsStore);
        serviceFactory = () =>
            new(
                compressionService,
                engineCatalogService,
                historyService,
                settingsStore,
                previewRenderer);
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
                services.PreviewRenderer);

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
        IPreviewRenderer? PreviewRenderer);
}
