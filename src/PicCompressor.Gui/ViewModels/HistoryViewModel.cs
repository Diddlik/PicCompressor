using System.Collections.ObjectModel;
using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

public sealed class HistoryEntryViewModel : ObservableObject
{
    public HistoryEntryViewModel(HistoryRecord record, Func<HistoryEntryViewModel, Task>? onDelete = null)
    {
        Record = record;
        DeleteCommand = new AsyncRelayCommand(
            () => onDelete?.Invoke(this) ?? Task.CompletedTask,
            () => onDelete is not null);
    }

    public HistoryRecord Record { get; }

    public AsyncRelayCommand DeleteCommand { get; }

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

    /// <summary>Eingesparte Bytes als Kachel-Pille (UI-Doc 05).</summary>
    public string SavedText => Record.OutputSizeBytes is long size
        ? ByteFormat.Describe(Math.Max(0, Record.InputSizeBytes - size))
        : ByteFormat.NotApplicable;

    /// <summary>„vorher → nachher“ als ein Wert (Pfeil bleibt aus dem XAML heraus).</summary>
    public string BeforeAfter => $"{Before} → {After}";

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

    /// <summary>Zugänglicher Name des Lösch-Buttons; nennt die betroffene Datei.</summary>
    public string DeleteAccessibleName =>
        Localizer.Instance.Format("Hist_DeleteEntry", FileName);
}

public sealed class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService historyService;
    private string search = string.Empty;

    public HistoryViewModel(IHistoryService historyService)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        this.historyService = historyService;
        ClearCommand = new AsyncRelayCommand(
            () => ClearAsync(CancellationToken.None),
            () => Entries.Count > 0);
    }

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public AsyncRelayCommand ClearCommand { get; }

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
            return Localizer.Instance.Format(
                "Hist_Summary",
                Entries.Count,
                ByteFormat.Describe(TotalSavedBytes));
        }
    }

    private long TotalSavedBytes => Math.Max(
        0,
        Entries
            .Where(entry => entry.Record.OutputSizeBytes is not null)
            .Sum(entry => entry.Record.InputSizeBytes - entry.Record.OutputSizeBytes!.Value));

    /// <summary>Kacheln des Arbeitsbereichs (UI-Doc 03): insgesamt gesparter Speicher.</summary>
    public string TotalSavedText => ByteFormat.Describe(TotalSavedBytes);

    /// <summary>Anzahl erfolgreich komprimierter Dateien.</summary>
    public string FilesCompressedText =>
        Entries.Count(entry => entry.IsSuccess).ToString(Localizer.Instance.Culture);

    /// <summary>Beste Einzelersparnis; leerer Verlauf ergibt „0%“.</summary>
    public string BestSavingText
    {
        get
        {
            var best = Entries
                .Where(entry => entry.Record.OutputSizeBytes is long size && entry.Record.InputSizeBytes > 0)
                .OrderBy(entry => (double)entry.Record.OutputSizeBytes!.Value / entry.Record.InputSizeBytes)
                .FirstOrDefault();
            return best?.Savings ?? "0%";
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var records = await historyService.GetAsync(cancellationToken).ConfigureAwait(true);

        Entries.Clear();
        foreach (var record in records)
        {
            Entries.Add(CreateEntry(record));
        }

        RaiseAll();
    }

    public async Task AppendAsync(HistoryRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var stored = await historyService.AppendAsync(record, cancellationToken).ConfigureAwait(true);
        Entries.Insert(0, CreateEntry(stored));
        RaiseAll();
    }

    /// <summary>
    /// Löscht einen einzelnen Eintrag (Abschnitt 13.1). Die Zeile verschwindet erst,
    /// wenn der Speicher den Löschvorgang bestätigt hat.
    /// </summary>
    public async Task DeleteAsync(HistoryEntryViewModel entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await historyService.DeleteAsync(entry.Record.Id, cancellationToken).ConfigureAwait(true);
        Entries.Remove(entry);
        RaiseAll();
    }

    /// <summary>
    /// Löscht den gesamten Verlauf (Abschnitt 13.1). Die Anzeige wird erst geleert,
    /// wenn der Speicher den Löschvorgang bestätigt hat.
    /// </summary>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await historyService.ClearAsync(cancellationToken).ConfigureAwait(true);
        Entries.Clear();
        RaiseAll();
    }

    private HistoryEntryViewModel CreateEntry(HistoryRecord record) =>
        new(record, entry => DeleteAsync(entry, CancellationToken.None));

    private void RaiseAll()
    {
        Raise(nameof(VisibleEntries));
        Raise(nameof(IsEmpty));
        Raise(nameof(Summary));
        Raise(nameof(TotalSavedText));
        Raise(nameof(FilesCompressedText));
        Raise(nameof(BestSavingText));
        ClearCommand.RaiseCanExecuteChanged();
    }
}
