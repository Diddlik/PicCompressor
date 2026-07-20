using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Navigation_selects_exactly_one_view()
    {
        var main = Create();

        main.ShowSettingsCommand.Execute(null);

        Assert.True(main.IsSettings);
        Assert.False(main.IsDashboard);
        Assert.False(main.IsHistory);
        Assert.False(main.IsCompare);
    }

    [Theory]
    [InlineData(1400, false, false)]
    [InlineData(900, false, true)]
    [InlineData(600, true, true)]
    public void Width_maps_to_layout_stages(double width, bool compact, bool narrow)
    {
        var main = Create();

        main.ApplyWidth(width);

        Assert.Equal(compact, main.IsCompact);
        Assert.Equal(narrow, main.IsNarrow);
        Assert.Equal(!compact, main.ShowNavigationLabels);
    }

    [Fact]
    public void Compact_rail_is_narrower_than_the_expanded_one()
    {
        var main = Create();

        main.ApplyWidth(1400);
        var wide = main.NavigationRailWidth;
        main.ApplyWidth(500);

        Assert.True(main.NavigationRailWidth < wide);
    }

    [Fact]
    public async Task Unavailable_engines_do_not_prevent_startup_and_state_a_reason()
    {
        var main = Create();

        await main.InitializeAsync(CancellationToken.None);

        Assert.False(main.Settings.IsSelectedEngineAvailable);
        Assert.False(string.IsNullOrWhiteSpace(main.EngineStatus));
        Assert.False(string.IsNullOrWhiteSpace(main.Settings.EngineAvailabilityText));
    }

    [Fact]
    public async Task A_failing_catalogue_does_not_prevent_startup()
    {
        var main = new MainWindowViewModel(
            new UnconfiguredCompressionService(),
            new ThrowingEngineCatalogService(),
            new InMemoryHistoryService());

        await main.InitializeAsync(CancellationToken.None);

        Assert.Contains("catalogue unreachable", main.EngineStatus);
        Assert.False(main.Settings.IsSelectedEngineAvailable);
    }

    [Fact]
    public async Task Available_engine_is_reported_as_available()
    {
        var main = new MainWindowViewModel(
            new UnconfiguredCompressionService(),
            FakeEngineCatalogService.JpegliAvailable(),
            new InMemoryHistoryService());

        await main.InitializeAsync(CancellationToken.None);

        Assert.True(main.Settings.IsSelectedEngineAvailable);
        Assert.Null(main.Settings.EngineAvailabilityText);
    }

    [Fact]
    public async Task Engine_selection_is_never_switched_silently()
    {
        var main = new MainWindowViewModel(
            new UnconfiguredCompressionService(),
            FakeEngineCatalogService.JpegliAvailable(),
            new InMemoryHistoryService());
        await main.InitializeAsync(CancellationToken.None);

        main.Settings.IsGuetzli = true;

        Assert.Equal(EngineIds.Guetzli, main.Settings.EngineId);
        Assert.False(main.Settings.IsSelectedEngineAvailable);
    }

    [Fact]
    public void Status_summary_is_localized_and_counts_terminal_jobs()
    {
        var previous = Localizer.Instance.Language;
        try
        {
            var main = Create();
            Localizer.Instance.Language = AppLanguage.English;
            Assert.Equal("No jobs", main.StatusSummary);

            Localizer.Instance.Language = AppLanguage.German;
            Assert.Equal("Keine Jobs", main.StatusSummary);
        }
        finally
        {
            Localizer.Instance.Language = previous;
        }
    }

    [Fact]
    public void Workspace_settings_and_compare_share_one_instance()
    {
        var main = new MainWindowViewModel(
            new UnconfiguredCompressionService(),
            new UnconfiguredEngineCatalogService(),
            new RecordingHistoryService());

        // Die Schnelleinstellungen des Arbeitsbereichs sind dieselben Einstellungen, mit denen
        // der Vergleich probeweise komprimiert. Zwei Instanzen zeigten sonst verschiedene
        // Ergebnisse für dieselbe Datei.
        Assert.Same(main.Settings, main.Dashboard.Settings);
    }

    [Fact]
    public void Every_queued_file_can_be_compared_without_being_converted_first()
    {
        var main = new MainWindowViewModel(
            new UnconfiguredCompressionService(),
            new UnconfiguredEngineCatalogService(),
            new RecordingHistoryService());

        var queued = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 10);
        var succeeded = new QueueItemViewModel("b.jpg", EngineIds.Jpegli, 10);
        succeeded.ApplyOutcome(
            new CompressionOutcome(
                JobStatus.Succeeded, "b.jpg", "b.out.jpg", 10, 5, true, null, null, null));

        main.Dashboard.Queue.Add(queued);
        main.Dashboard.Queue.Add(succeeded);

        // Auch die noch nicht komprimierte Datei ist vergleichbar; ihr Ergebnis entsteht bei
        // Bedarf im Speicher.
        Assert.Equal(2, main.Compare.Candidates.Count);
        Assert.Same(queued, main.Compare.Selected);
    }

    [Fact]
    public async Task Clearing_the_history_empties_store_and_view()
    {
        var service = new RecordingHistoryService();
        var history = new HistoryViewModel(service);
        await history.AppendAsync(
            new HistoryRecord(
                DateTimeOffset.UtcNow,
                "a.jpg",
                EngineIds.Jpegli,
                10,
                5,
                JobStatus.Succeeded,
                null),
            CancellationToken.None);
        Assert.True(history.ClearCommand.CanExecute(null));

        await history.ClearAsync(CancellationToken.None);

        Assert.Empty(history.Entries);
        Assert.Empty(service.Records);
        // Ein leerer Verlauf lässt sich nicht erneut löschen.
        Assert.False(history.ClearCommand.CanExecute(null));
    }

    private static MainWindowViewModel Create() =>
        new(
            new UnconfiguredCompressionService(),
            new UnconfiguredEngineCatalogService(),
            new InMemoryHistoryService());
}
