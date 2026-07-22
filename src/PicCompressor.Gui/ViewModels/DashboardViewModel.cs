using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png"];

    private readonly ICompressionService compressionService;

    private CancellationTokenSource? runCancellation;
    private bool recurseFolders = true;
    private string? dropHint;

    public DashboardViewModel(SettingsViewModel settings, ICompressionService compressionService)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(compressionService);

        Settings = settings;
        this.compressionService = compressionService;

        Queue.CollectionChanged += OnQueueChanged;

        CompressAllCommand = new AsyncRelayCommand(CompressAllAsync, () => HasPendingJobs);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        ClearCompletedCommand = new RelayCommand(ClearCompleted, () => HasCompletedJobs);
        RetryFailedCommand = new RelayCommand(RetryFailed, () => HasRetryableJobs);
        RemoveAllCommand = new RelayCommand(RemoveAll, () => Queue.Count > 0 && !IsRunning);
    }

    /// <summary>Meldet jeden abgeschlossenen Job, damit Verlauf und Vergleich nachziehen können.</summary>
    public event EventHandler<QueueItemViewModel>? JobCompleted;

    public SettingsViewModel Settings { get; }

    public ObservableCollection<QueueItemViewModel> Queue { get; } = [];

    public AsyncRelayCommand CompressAllCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ClearCompletedCommand { get; }

    public RelayCommand RetryFailedCommand { get; }

    public RelayCommand RemoveAllCommand { get; }

    public bool RecurseFolders
    {
        get => recurseFolders;
        set => SetProperty(ref recurseFolders, value);
    }

    public bool IsRunning => runCancellation is not null;

    public bool IsQueueEmpty => Queue.Count == 0;

    public bool HasPendingJobs => !IsRunning && Queue.Any(item => !item.IsTerminal);

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
    /// Nimmt Dateien und Ordner aus Drag-and-drop oder Dateiauswahl auf. Die Endung entscheidet
    /// hier nur über die Vorauswahl; die inhaltliche Formatprüfung nach Abschnitt 7.1 gehört in
    /// die Anwendungsschicht und findet beim Ausführen statt.
    /// </summary>
    public int AddPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var added = 0;
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                added += AddDirectory(path);
            }
            else if (File.Exists(path) && IsSupported(path) && AddFile(path))
            {
                added++;
            }
        }

        DropHint = added == 0
            ? Localizer.Instance["Dash_NoSupportedFiles"]
            : null;
        return added;
    }

    private int AddDirectory(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = RecurseFolders,
            // Abschnitt 7.3: Verzeichnislinks werden standardmäßig nicht verfolgt.
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            IgnoreInaccessible = true
        };

        var added = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", options))
        {
            if (IsSupported(file) && AddFile(file))
            {
                added++;
            }
        }

        return added;
    }

    private bool AddFile(string path)
    {
        if (Queue.Any(item => string.Equals(item.InputPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Der Eintrag bleibt sichtbar; die belastbare Prüfung folgt beim Ausführen.
            size = 0;
        }

        Queue.Add(new QueueItemViewModel(path, Settings.EngineId, size));
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
    }

    private void RaiseRunState()
    {
        Raise(nameof(IsRunning));
        RaiseQueueState();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
