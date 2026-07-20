using PicCompressor.Application;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

public enum AppView
{
    Dashboard,
    Settings,
    History,
    Compare
}

public sealed class MainWindowViewModel : ObservableObject
{
    /// <summary>Unterhalb dieser Fensterbreite klappt die Navigationsschiene auf Symbole zusammen.</summary>
    public const double CompactWidthThreshold = 820;

    /// <summary>Unterhalb dieser Breite stapelt der Arbeitsbereich Warteschlange und Schnelleinstellungen.</summary>
    public const double NarrowWidthThreshold = 1000;

    private readonly IEngineCatalogService engineCatalogService;

    private AppView view = AppView.Dashboard;
    private bool isCompact;
    private bool isNarrow;
    private string? engineStatus;
    private string? historyWarning;

    public MainWindowViewModel(
        ICompressionService compressionService,
        IEngineCatalogService engineCatalogService,
        IHistoryService historyService,
        IApplicationSettingsStore? settingsStore = null,
        IPreviewRenderer? previewRenderer = null)
    {
        ArgumentNullException.ThrowIfNull(compressionService);
        ArgumentNullException.ThrowIfNull(engineCatalogService);
        ArgumentNullException.ThrowIfNull(historyService);

        this.engineCatalogService = engineCatalogService;

        Settings = new SettingsViewModel(settingsStore);
        Dashboard = new DashboardViewModel(Settings, compressionService);
        History = new HistoryViewModel(historyService);
        Compare = new CompareViewModel(previewRenderer);

        Dashboard.JobCompleted += OnJobCompleted;
        Dashboard.PropertyChanged += (_, _) => Raise(nameof(StatusSummary));

        ShowDashboardCommand = new RelayCommand(() => View = AppView.Dashboard);
        ShowSettingsCommand = new RelayCommand(() => View = AppView.Settings);
        ShowHistoryCommand = new RelayCommand(() => View = AppView.History);
        ShowCompareCommand = new RelayCommand(() => View = AppView.Compare);
    }

    public SettingsViewModel Settings { get; }
    public DashboardViewModel Dashboard { get; }
    public HistoryViewModel History { get; }
    public CompareViewModel Compare { get; }

    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ShowHistoryCommand { get; }
    public RelayCommand ShowCompareCommand { get; }

    public AppView View
    {
        get => view;
        set
        {
            if (SetProperty(ref view, value))
            {
                Raise(nameof(IsDashboard));
                Raise(nameof(IsSettings));
                Raise(nameof(IsHistory));
                Raise(nameof(IsCompare));
            }
        }
    }

    public bool IsDashboard
    {
        get => View is AppView.Dashboard;
        set { if (value) { View = AppView.Dashboard; } }
    }

    public bool IsSettings
    {
        get => View is AppView.Settings;
        set { if (value) { View = AppView.Settings; } }
    }

    public bool IsHistory
    {
        get => View is AppView.History;
        set { if (value) { View = AppView.History; } }
    }

    public bool IsCompare
    {
        get => View is AppView.Compare;
        set { if (value) { View = AppView.Compare; } }
    }

    public bool IsCompact
    {
        get => isCompact;
        private set
        {
            if (SetProperty(ref isCompact, value))
            {
                Raise(nameof(ShowNavigationLabels));
                Raise(nameof(NavigationRailWidth));
            }
        }
    }

    public bool IsNarrow
    {
        get => isNarrow;
        private set => SetProperty(ref isNarrow, value);
    }

    public bool ShowNavigationLabels => !IsCompact;

    public double NavigationRailWidth => IsCompact ? 56 : 196;

    /// <summary>Ordnet der Fensterbreite die Layoutstufen zu; die Ansicht meldet nur die Breite.</summary>
    public void ApplyWidth(double width)
    {
        IsCompact = width < CompactWidthThreshold;
        IsNarrow = width < NarrowWidthThreshold;
    }

    /// <summary>
    /// Ergebnis der Engine-Erkennung beim Start (Abschnitt 4.2). Eine nicht verfügbare Engine
    /// verhindert den Start nicht, wird aber mit konkreter Ursache angezeigt.
    /// </summary>
    public string? EngineStatus
    {
        get => engineStatus;
        private set => SetProperty(ref engineStatus, value);
    }

    /// <summary>
    /// Meldung, wenn ein Ergebnis nicht in den Verlauf geschrieben werden konnte.
    /// Ein Persistenzfehler wird als eigene Warnung ausgewiesen und verändert das
    /// Kompressionsergebnis nicht (Abschnitt 14.4).
    /// </summary>
    public string? HistoryWarning
    {
        get => historyWarning;
        private set => SetProperty(ref historyWarning, value);
    }

    public string StatusSummary
    {
        get
        {
            var total = Dashboard.Queue.Count;
            if (total == 0)
            {
                return Localizer.Instance["Status_NoJobs"];
            }

            var done = Dashboard.Queue.Count(item => item.IsTerminal);
            return Localizer.Instance.Format("Status_Completed", done, total);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await LoadEnginesAsync(cancellationToken).ConfigureAwait(true);
        await History.LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadEnginesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EngineAvailability> engines;
        try
        {
            engines = await engineCatalogService
                .GetEnginesAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Eine fehlgeschlagene Erkennung darf den Start nicht verhindern (Abschnitt 4.2).
            EngineStatus = exception.Message;
            Settings.ApplyEngineAvailability([]);
            return;
        }

        Settings.ApplyEngineAvailability(engines);

        var available = engines.Where(engine => engine.IsAvailable).ToList();
        EngineStatus = available.Count > 0
            ? Localizer.Instance.Format(
                "Status_EnginesAvailable",
                string.Join(", ", available.Select(engine => EngineIds.DisplayName(engine.EngineId))))
            : engines.FirstOrDefault()?.UnavailableReason
              ?? Localizer.Instance["Error_NoEngineCatalog"];
    }

    private async void OnJobCompleted(object? sender, QueueItemViewModel item)
    {
        Compare.Offer(item);
        Raise(nameof(StatusSummary));

        try
        {
            await History
                .AppendAsync(
                    new HistoryRecord(
                        DateTimeOffset.UtcNow,
                        item.FileName,
                        item.EngineId,
                        item.InputSizeBytes,
                        item.OutputSizeBytes,
                        item.Status,
                        item.ErrorCategory),
                    CancellationToken.None)
                .ConfigureAwait(true);
            HistoryWarning = null;
        }
        catch (Exception exception)
        {
            // Ein Persistenzfehler darf ein korrekt erzeugtes Bild nicht nachträglich als
            // Kompressionsfehler deklarieren (Abschnitt 14.4). Der Job bleibt unverändert,
            // der Fehler wird aber als eigene Warnung sichtbar gemacht.
            HistoryWarning = Localizer.Instance.Format(
                "Warning_HistoryNotRecorded",
                exception.Message);
        }
    }
}
