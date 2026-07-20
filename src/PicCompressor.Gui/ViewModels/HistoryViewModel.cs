using System.Collections.ObjectModel;
using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

public sealed class HistoryEntryViewModel(HistoryRecord record) : ObservableObject
{
    public HistoryRecord Record { get; } = record;

    public string FileName => Record.FileName;

    /// <summary>Zeitpunkt in der aktiven Kultur (Abschnitt 11.2).</summary>
    public string Timestamp =>
        Record.CompletedAt.LocalDateTime.ToString("g", Localizer.Instance.Culture);

    public string EngineLabel => EngineIds.DisplayName(Record.EngineId);

    public string Before => ByteFormat.Describe(Record.InputSizeBytes);

    public string After => Record.OutputSizeBytes is long size
        ? ByteFormat.Describe(size)
        : ByteFormat.NotApplicable;

    public string Savings => Record.OutputSizeBytes is long size
        ? ByteFormat.DescribeSavings(Record.InputSizeBytes, size)
        : ByteFormat.NotApplicable;

    public bool IsSuccess => Record.Status is JobStatus.Succeeded;

    /// <summary>
    /// Erfolg wird übersetzt, die Fehlerkategorie nicht — sie ist ein stabiler Bezeichner
    /// (Abschnitt 6.4).
    /// </summary>
    public string StatusLabel => Record.Status switch
    {
        JobStatus.Succeeded => Localizer.Instance["Hist_Success"],
        JobStatus.Canceled => Localizer.Instance["Job_Canceled"],
        _ => Record.ErrorCategory?.ToString() ?? Localizer.Instance["Hist_Error"]
    };

    public string AccessibleSummary => string.Join(
        " · ",
        Timestamp,
        FileName,
        EngineLabel,
        $"{Before} → {After}",
        Savings,
        StatusLabel);
}

public sealed class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService historyService;
    private string search = string.Empty;

    public HistoryViewModel(IHistoryService historyService)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        this.historyService = historyService;
    }

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public string Search
    {
        get => search;
        set
        {
            if (SetProperty(ref search, value))
            {
                Raise(nameof(VisibleEntries));
                Raise(nameof(IsEmpty));
            }
        }
    }

    public IReadOnlyList<HistoryEntryViewModel> VisibleEntries => string.IsNullOrWhiteSpace(Search)
        ? Entries
        : [.. Entries.Where(entry =>
            entry.FileName.Contains(Search, StringComparison.CurrentCultureIgnoreCase))];

    public bool IsEmpty => VisibleEntries.Count == 0;

    /// <summary>
    /// Ohne verdrahteten Verlaufsspeicher zeigt die Oberfläche das offen an, statt einen leeren
    /// Verlauf als vollständig auszugeben (Abschnitt 13.1).
    /// </summary>
    public bool IsPersistent => historyService.IsAvailable;

    public string Summary
    {
        get
        {
            var saved = Entries
                .Where(entry => entry.Record.OutputSizeBytes is not null)
                .Sum(entry => entry.Record.InputSizeBytes - entry.Record.OutputSizeBytes!.Value);
            return Localizer.Instance.Format(
                "Hist_Summary",
                Entries.Count,
                ByteFormat.Describe(Math.Max(0, saved)));
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var records = await historyService.GetAsync(cancellationToken).ConfigureAwait(true);

        Entries.Clear();
        foreach (var record in records)
        {
            Entries.Add(new HistoryEntryViewModel(record));
        }

        RaiseAll();
    }

    public async Task AppendAsync(HistoryRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await historyService.AppendAsync(record, cancellationToken).ConfigureAwait(true);
        Entries.Insert(0, new HistoryEntryViewModel(record));
        RaiseAll();
    }

    private void RaiseAll()
    {
        Raise(nameof(VisibleEntries));
        Raise(nameof(IsEmpty));
        Raise(nameof(Summary));
    }
}
