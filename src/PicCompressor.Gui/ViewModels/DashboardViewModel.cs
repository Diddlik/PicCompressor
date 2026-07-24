using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly ICompressionService compressionService;
    private readonly IInputDiscovery inputDiscovery;
    private readonly IFileActionService fileActions;
    private readonly ThumbnailCache? thumbnails;
    private readonly IClipboardImportService clipboardImport;
    private readonly INotificationService notifications;

    private CancellationTokenSource? runCancellation;
    private CancellationTokenSource? discoveryCancellation;
    private bool recurseFolders = true;
    private bool isDiscovering;
    private int discoveredCount;
    private string? dropHint;

    public DashboardViewModel(
        SettingsViewModel settings,
        ICompressionService compressionService,
        IInputDiscovery? inputDiscovery = null,
        IFileActionService? fileActions = null,
        ThumbnailCache? thumbnails = null,
        IClipboardImportService? clipboardImport = null,
        INotificationService? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(compressionService);

        Settings = settings;
        this.compressionService = compressionService;
        this.inputDiscovery = inputDiscovery ?? new UnconfiguredInputDiscovery();
        this.fileActions = fileActions ?? new UnconfiguredFileActionService();
        // Ohne Vorschaudienst bleibt die Liste ohne Bilder; sie ist deswegen nicht weniger bedienbar.
        this.thumbnails = thumbnails;
        this.clipboardImport = clipboardImport ?? new UnconfiguredClipboardImportService();
        this.notifications = notifications ?? new UnconfiguredNotificationService();

        Queue.CollectionChanged += OnQueueChanged;

        CompressAllCommand = new AsyncRelayCommand(CompressAllAsync, () => HasPendingJobs);
        PasteCommand = new AsyncRelayCommand(PasteAsync, () => !IsRunning && !IsDiscovering);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        CancelDiscoveryCommand = new RelayCommand(CancelDiscovery, () => IsDiscovering);
        ClearCompletedCommand = new RelayCommand(ClearCompleted, () => HasCompletedJobs);
        RetryFailedCommand = new RelayCommand(RetryFailed, () => HasRetryableJobs);
        RemoveAllCommand = new RelayCommand(RemoveAll, () => Queue.Count > 0 && !IsRunning);

        CompareItemCommand = new RelayCommand<QueueItemViewModel>(
            item => CompareRequested?.Invoke(this, item));
        OpenItemCommand = new RelayCommand<QueueItemViewModel>(
            item => _ = fileActions!.OpenFileAsync(item.OutputPath!),
            item => item.OutputPublished && item.OutputPath is not null);
        RevealItemCommand = new RelayCommand<QueueItemViewModel>(
            item => _ = fileActions!.RevealInFolderAsync(TargetPath(item)));
        CopyPathItemCommand = new RelayCommand<QueueItemViewModel>(
            item => _ = fileActions!.CopyPathAsync(TargetPath(item)));
        RemoveItemCommand = new RelayCommand<QueueItemViewModel>(RemoveItem, _ => !IsRunning);
        RetryItemCommand = new RelayCommand<QueueItemViewModel>(
            RetryItem,
            item => !IsRunning && item.Status is JobStatus.Failed or JobStatus.Canceled);
    }

    /// <summary>Meldet jeden abgeschlossenen Job, damit Verlauf und Vergleich nachziehen können.</summary>
    public event EventHandler<QueueItemViewModel>? JobCompleted;

    /// <summary>Meldet den Wunsch, eine Datei zu vergleichen; die Shell wechselt in die Vergleichsansicht.</summary>
    public event EventHandler<QueueItemViewModel>? CompareRequested;

    public SettingsViewModel Settings { get; }

    public ObservableCollection<QueueItemViewModel> Queue { get; } = [];

    public AsyncRelayCommand CompressAllCommand { get; }

    public AsyncRelayCommand PasteCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ClearCompletedCommand { get; }

    public RelayCommand RetryFailedCommand { get; }

    public RelayCommand RemoveAllCommand { get; }

    public RelayCommand CancelDiscoveryCommand { get; }

    public RelayCommand<QueueItemViewModel> CompareItemCommand { get; }

    public RelayCommand<QueueItemViewModel> OpenItemCommand { get; }

    public RelayCommand<QueueItemViewModel> RevealItemCommand { get; }

    public RelayCommand<QueueItemViewModel> CopyPathItemCommand { get; }

    public RelayCommand<QueueItemViewModel> RemoveItemCommand { get; }

    public RelayCommand<QueueItemViewModel> RetryItemCommand { get; }

    public bool RecurseFolders
    {
        get => recurseFolders;
        set => SetProperty(ref recurseFolders, value);
    }

    /// <summary>Läuft gerade eine Ordner-Discovery? Die Enumeration selbst liegt abseits des UI-Threads.</summary>
    public bool IsDiscovering
    {
        get => isDiscovering;
        private set
        {
            if (SetProperty(ref isDiscovering, value))
            {
                Raise(nameof(DiscoveringLabel));
                CancelDiscoveryCommand.RaiseCanExecuteChanged();
                CompressAllCommand.RaiseCanExecuteChanged();
                PasteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Bisher gefundene Dateien während einer laufenden Discovery.</summary>
    public int DiscoveredCount
    {
        get => discoveredCount;
        private set
        {
            if (SetProperty(ref discoveredCount, value))
            {
                Raise(nameof(DiscoveringLabel));
            }
        }
    }

    public string? DiscoveringLabel => IsDiscovering
        ? Localizer.Instance.Format("Dash_Discovering", DiscoveredCount)
        : null;

    public bool IsRunning => runCancellation is not null;

    public bool IsQueueEmpty => Queue.Count == 0;

    public bool HasPendingJobs => !IsRunning && !IsDiscovering && Queue.Any(item => !item.IsTerminal);

    public bool HasCompletedJobs => !IsRunning && Queue.Any(item => item.IsTerminal);

    public bool HasRetryableJobs => !IsRunning
        && Queue.Any(item => item.Status is JobStatus.Failed or JobStatus.Canceled);

    public string QueueCountLabel => Localizer.Instance.Format("Dash_QueueCount", Queue.Count);

    /// <summary>Rückmeldung zur letzten Ablage, etwa wenn nichts Unterstütztes dabei war.</summary>
    public string? DropHint
    {
        get => dropHint;
        private set
        {
            if (SetProperty(ref dropHint, value))
            {
                Raise(nameof(HasDropHint));
            }
        }
    }

    public bool HasDropHint => !string.IsNullOrEmpty(DropHint);

    /// <summary>
    /// Nimmt Dateien und Ordner aus Drag-and-drop oder Auswahl auf. Die Ordner-Discovery läuft
    /// über den gemeinsamen Application-Anwendungsfall abseits des UI-Threads (MP-002); die
    /// Endung entscheidet dort nur über die Vorauswahl, die inhaltliche Formatprüfung nach
    /// Abschnitt 7.1 folgt beim Ausführen.
    /// </summary>
    public async Task<int> AddPathsAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Existenz der Wurzeln auf dem UI-Thread prüfen (wenige Pfade); die teure Enumeration
        // bleibt off-thread. So bricht ein veralteter Pfad die Aufnahme nicht ab.
        var roots = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
        if (roots.Count == 0)
        {
            DropHint = Localizer.Instance["Dash_NoSupportedFiles"];
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        discoveryCancellation = cancellation;
        DiscoveredCount = 0;
        IsDiscovering = true;
        try
        {
            var excluded = Settings.UsesCustomDirectory ? Settings.OutputDirectory : null;
            var progress = new Progress<int>(count => DiscoveredCount = count);
            IReadOnlyList<DiscoveredInput> found;
            try
            {
                found = await inputDiscovery
                    .DiscoverAsync(roots, RecurseFolders, excluded, progress, cancellation.Token)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                DropHint = Localizer.Instance["Dash_DiscoveryCanceled"];
                return 0;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                DropHint = Localizer.Instance["Dash_NoSupportedFiles"];
                return 0;
            }

            var added = 0;
            foreach (var input in found)
            {
                if (AddFile(input.Path, input.FileSizeBytes))
                {
                    added++;
                }
            }

            DropHint = added == 0 ? Localizer.Instance["Dash_NoSupportedFiles"] : null;
            return added;
        }
        finally
        {
            discoveryCancellation = null;
            IsDiscovering = false;
            DiscoveredCount = 0;
        }
    }

    /// <summary>
    /// Reiht Dateilisten und Bilddaten aus der Zwischenablage ein (MP-003). Der Host-Adapter legt
    /// Bilddaten zuvor als verwaltete temporäre Eingabe ab und meldet deren Pfad; eingereiht wird
    /// über denselben Weg wie Ablage und Auswahl, also mit voller Prüfung nach Abschnitt 7.1.
    /// <c>internal</c> statt <c>private</c>, damit Tests den Abschluss abwarten können.
    /// </summary>
    internal async Task PasteAsync()
    {
        var paths = await clipboardImport
            .ReadImportPathsAsync(CancellationToken.None)
            .ConfigureAwait(true);
        if (paths.Count == 0)
        {
            DropHint = Localizer.Instance["Dash_ClipboardEmpty"];
            return;
        }

        await AddPathsAsync(paths).ConfigureAwait(true);
    }

    private bool AddFile(string path, long size)
    {
        if (Queue.Any(item => string.Equals(item.InputPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Queue.Add(new QueueItemViewModel(path, Settings.EngineId, size, thumbnails));
        return true;
    }

    /// <summary>
    /// Arbeitet die Warteschlange ab. <c>internal</c> statt <c>private</c>, damit Tests
    /// den Abschluss abwarten können: <see cref="AsyncRelayCommand.Execute"/> ist
    /// <c>async void</c> und gibt keine abwartbare Aufgabe zurück.
    /// </summary>
    internal async Task CompressAllAsync()
    {
        var pending = Queue.Where(item => !item.IsTerminal).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        runCancellation = cancellation;
        RaiseRunState();

        try
        {
            foreach (var item in pending)
            {
                item.ResetForRun();
                item.Status = JobStatus.Validating;
            }

            var engineSettings = Settings.TryBuildEngineSettings();
            if (engineSettings is null)
            {
                foreach (var item in pending)
                {
                    item.ApplyOutcome(
                        CompressionOutcome.Failed(
                            item.InputPath,
                            item.InputSizeBytes,
                            CompressionErrorCategory.EngineUnavailable,
                            Localizer.Instance.Format(
                                "Engine_Unavailable",
                                EngineIds.DisplayName(Settings.EngineId))));
                }
            }
            else
            {
                var progress = new Progress<CompressionBatchProgress>(
                    update => pending[update.Index].ApplyProgress(update.Progress));
                var outcomes = await compressionService
                    .CompressBatchAsync(
                        pending.Select(item => BuildRequest(item, engineSettings)).ToArray(),
                        Settings.ParallelJobs,
                        progress,
                        cancellation.Token)
                    .ConfigureAwait(true);
                if (outcomes.Count != pending.Count)
                {
                    throw new InvalidOperationException(
                        "Compression service returned an unexpected result count.");
                }

                for (var index = 0; index < pending.Count; index++)
                {
                    pending[index].ApplyOutcome(outcomes[index]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var item in pending.Where(item => !item.IsTerminal))
            {
                item.ApplyOutcome(CompressionOutcome.Canceled(item.InputPath, item.InputSizeBytes));
            }
        }
        catch (Exception exception)
        {
            // Unerwartete Ausnahmen werden an der Shell-Grenze in einen Produktfehler übersetzt
            // (Abschnitt 14.3) und nie als Erfolg ausgegeben.
            foreach (var item in pending.Where(item => !item.IsTerminal))
            {
                item.ApplyOutcome(
                    CompressionOutcome.Failed(
                        item.InputPath,
                        item.InputSizeBytes,
                        CompressionErrorCategory.Unexpected,
                        exception.Message));
            }
        }
        finally
        {
            foreach (var item in pending)
            {
                JobCompleted?.Invoke(this, item);
            }

            runCancellation = null;
            RaiseRunState();
        }

        await NotifyBatchCompleteAsync(pending).ConfigureAwait(true);
    }

    /// <summary>
    /// Meldet den Abschluss eines Laufs als Benachrichtigung (MP-003). Der Text nennt die Zahl
    /// erfolgreicher und fehlgeschlagener Jobs; ein Fehlschlag markiert die Meldung als Fehler.
    /// Die Anzeige ist capability-gesteuert und rein additiv — ohne verdrahteten Host geschieht
    /// nichts.
    /// </summary>
    private async Task NotifyBatchCompleteAsync(IReadOnlyList<QueueItemViewModel> processed)
    {
        var failed = processed.Count(item => item.Status is JobStatus.Failed);
        var succeeded = processed.Count(item => item.Status is JobStatus.Succeeded);
        await notifications
            .ShowAsync(
                Localizer.Instance["Notify_BatchTitle"],
                Localizer.Instance.Format("Notify_BatchBody", succeeded, failed),
                isError: failed > 0)
            .ConfigureAwait(true);
    }

    private CompressionRequest BuildRequest(
        QueueItemViewModel item,
        CompressionEngineSettings engineSettings)
    {
        // Der Vergleich braucht später denselben Hintergrund wie der Lauf.
        item.AlphaBackground = Settings.AlphaBackground;
        return new(
            item.InputPath,
            engineSettings,
            Settings.ExifPolicy,
            Settings.ColorProfilePolicy,
            Settings.AlphaBackground,
            Settings.CollisionPolicy,
            Settings.LargerOutputPolicy,
            Settings.UsesCustomDirectory ? Settings.OutputDirectory : null,
            Settings.Suffix,
            item.PredecessorJobId,
            Settings.MinimumSavingsPercent);
    }

    private void Cancel() => runCancellation?.Cancel();

    private void CancelDiscovery() => discoveryCancellation?.Cancel();

    /// <summary>Ziel einer Datei-Aktion: das veröffentlichte Ergebnis, sonst die Eingabe.</summary>
    private static string TargetPath(QueueItemViewModel item) =>
        item is { OutputPublished: true, OutputPath: string output } ? output : item.InputPath;

    private void RemoveItem(QueueItemViewModel item) => Queue.Remove(item);

    private void RetryItem(QueueItemViewModel item)
    {
        item.PrepareRetry();
        RaiseQueueState();
    }

    private void ClearCompleted()
    {
        foreach (var done in Queue.Where(item => item.IsTerminal).ToList())
        {
            Queue.Remove(done);
        }
    }

    private void RetryFailed()
    {
        foreach (var item in Queue.Where(
            item => item.Status is JobStatus.Failed or JobStatus.Canceled))
        {
            item.PrepareRetry();
        }

        RaiseQueueState();
    }

    private void RemoveAll() => Queue.Clear();

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.OfType<QueueItemViewModel>() ?? [])
        {
            item.PropertyChanged += OnItemChanged;
        }

        foreach (var item in e.OldItems?.OfType<QueueItemViewModel>() ?? [])
        {
            item.PropertyChanged -= OnItemChanged;
        }

        RaiseQueueState();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QueueItemViewModel.IsTerminal) or "" or null)
        {
            RaiseQueueState();
        }
    }

    private void RaiseQueueState()
    {
        Raise(nameof(IsQueueEmpty));
        Raise(nameof(QueueCountLabel));
        Raise(nameof(HasPendingJobs));
        Raise(nameof(HasCompletedJobs));
        Raise(nameof(HasRetryableJobs));
        CompressAllCommand.RaiseCanExecuteChanged();
        ClearCompletedCommand.RaiseCanExecuteChanged();
        RetryFailedCommand.RaiseCanExecuteChanged();
        RemoveAllCommand.RaiseCanExecuteChanged();
        // Pro-Zeilen-Aktionen hängen an Status und Laufzustand und werden mit neu bewertet.
        OpenItemCommand.RaiseCanExecuteChanged();
        RemoveItemCommand.RaiseCanExecuteChanged();
        RetryItemCommand.RaiseCanExecuteChanged();
    }

    private void RaiseRunState()
    {
        Raise(nameof(IsRunning));
        RaiseQueueState();
        CancelCommand.RaiseCanExecuteChanged();
        PasteCommand.RaiseCanExecuteChanged();
    }
}
