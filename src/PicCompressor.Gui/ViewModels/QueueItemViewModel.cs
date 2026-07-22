using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

public sealed class QueueItemViewModel : ObservableObject
{
    private readonly Lock statusGate = new();
    private JobStatus status = JobStatus.Queued;
    private double? progressPercent;
    private string? warning;
    private CompressionErrorCategory? errorCategory;
    private string? errorText;
    private long? outputSizeBytes;
    private string? outputPath;
    private bool outputPublished;

    public QueueItemViewModel(string inputPath, string engineId, long inputSizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentOutOfRangeException.ThrowIfNegative(inputSizeBytes);

        InputPath = inputPath;
        FileName = Path.GetFileName(inputPath);
        EngineId = engineId;
        InputSizeBytes = inputSizeBytes;
    }

    public string InputPath { get; }

    public string FileName { get; }

    public string EngineId { get; }

    /// <summary>Eigenname der Engine; bleibt unübersetzt (Abschnitt 4.3).</summary>
    public string EngineLabel => EngineIds.DisplayName(EngineId);

    public long InputSizeBytes { get; }

    /// <summary>
    /// Effektiver Alpha-Hintergrund des Laufs. Die Vorschau des Originals muss transparente
    /// Bereiche genauso auffüllen wie das Encoding (Abschnitt 8.1).
    /// </summary>
    public RgbColor AlphaBackground { get; set; } = RgbColor.White;

    public Guid? JobId { get; private set; }

    public Guid? PredecessorJobId { get; private set; }

    public JobStatus Status
    {
        get => status;
        set => SetStatus(value, leaveTerminal: false);
    }

    /// <summary>
    /// Setzt den Status unter Wahrung der Invariante aus Abschnitt 6.2: ein terminaler
    /// Zustand wird nur über <see cref="ResetForRun"/> verlassen. Der Vergleich und das
    /// Schreiben laufen unter einer Sperre, weil Fortschrittsberichte über
    /// <see cref="IProgress{T}"/> asynchron zugestellt werden und dem Endergebnis
    /// nachlaufen können (Abschnitt 14.4).
    /// </summary>
    private void SetStatus(JobStatus value, bool leaveTerminal)
    {
        lock (statusGate)
        {
            if (!leaveTerminal && IsTerminal)
            {
                return;
            }

            if (!SetProperty(ref status, value))
            {
                return;
            }
        }

        Raise(nameof(StatusLabel));
        Raise(nameof(IsIndeterminate));
        Raise(nameof(IsTerminal));
        Raise(nameof(IsRunning));
        Raise(nameof(CanRetry));
        Raise(nameof(AccessibleSummary));
    }

    /// <summary>
    /// Nur gesetzt, wenn die Engine einen belastbaren Wert liefert; sonst unbestimmt
    /// (Abschnitt 10.2 verbietet erfundene Prozentwerte).
    /// </summary>
    public double? ProgressPercent
    {
        get => progressPercent;
        set
        {
            if (SetProperty(ref progressPercent, value))
            {
                Raise(nameof(IsIndeterminate));
                Raise(nameof(AccessibleSummary));
            }
        }
    }

    public bool IsIndeterminate => ProgressPercent is null && IsRunning;

    public bool IsRunning =>
        Status is JobStatus.Validating
            or JobStatus.WaitingForResources
            or JobStatus.Encoding
            or JobStatus.Finalizing;

    public bool IsTerminal =>
        Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled;

    /// <summary>Eine Wiederholung ist nur nach einem Fehlschlag oder Abbruch sinnvoll (Abschnitt 6.1).</summary>
    public bool CanRetry => Status is JobStatus.Failed or JobStatus.Canceled;

    public string StatusLabel => Localizer.Instance[$"Job_{Status}"];

    public string? Warning
    {
        get => warning;
        private set
        {
            if (SetProperty(ref warning, value))
            {
                Raise(nameof(HasWarning));
                Raise(nameof(AccessibleSummary));
            }
        }
    }

    public bool HasWarning => !string.IsNullOrEmpty(Warning);

    public CompressionErrorCategory? ErrorCategory
    {
        get => errorCategory;
        private set
        {
            if (SetProperty(ref errorCategory, value))
            {
                Raise(nameof(HasError));
                Raise(nameof(ErrorSummary));
                Raise(nameof(AccessibleSummary));
            }
        }
    }

    public string? ErrorText
    {
        get => errorText;
        private set
        {
            if (SetProperty(ref errorText, value))
            {
                Raise(nameof(ErrorSummary));
                Raise(nameof(AccessibleSummary));
            }
        }
    }

    public bool HasError => ErrorCategory is not null;

    /// <summary>
    /// Stabile Fehlerkategorie plus Ursache. Der Kategoriebezeichner ist plattform- und
    /// sprachidentisch zu CLI, Verlauf und Logs (Abschnitt 6.4).
    /// </summary>
    public string? ErrorSummary => ErrorCategory is null
        ? null
        : string.IsNullOrEmpty(ErrorText)
            ? ErrorCategory.ToString()
            : $"{ErrorCategory}: {ErrorText}";

    public long? OutputSizeBytes
    {
        get => outputSizeBytes;
        private set
        {
            if (SetProperty(ref outputSizeBytes, value))
            {
                Raise(nameof(SizeSummary));
                Raise(nameof(AccessibleSummary));
            }
        }
    }

    public string? OutputPath
    {
        get => outputPath;
        private set => SetProperty(ref outputPath, value);
    }

    /// <summary>Nur ein veröffentlichtes Ergebnis darf verglichen werden (Abschnitt 11).</summary>
    public bool OutputPublished
    {
        get => outputPublished;
        private set
        {
            if (SetProperty(ref outputPublished, value))
            {
                Raise(nameof(CanCompare));
            }
        }
    }

    public bool CanCompare => OutputPublished && Status is JobStatus.Succeeded;

    public string SizeSummary => OutputSizeBytes is long output
        ? $"{ByteFormat.Describe(InputSizeBytes)} → {ByteFormat.Describe(output)} "
          + $"({ByteFormat.DescribeSavings(InputSizeBytes, output)})"
        : ByteFormat.Describe(InputSizeBytes);

    /// <summary>
    /// Zusammenfassung für Screenreader. Der Zustand wird damit nicht ausschließlich über
    /// Farbe vermittelt (Abschnitt 11).
    /// </summary>
    public string AccessibleSummary
    {
        get
        {
            var parts = new List<string> { FileName, StatusLabel, SizeSummary };
            if (HasWarning)
            {
                parts.Add(Warning!);
            }

            if (HasError)
            {
                parts.Add(ErrorSummary!);
            }

            return string.Join(" · ", parts);
        }
    }

    public void ResetForRun()
    {
        // Der einzige zulässige Weg aus einem terminalen Zustand.
        SetStatus(JobStatus.Queued, leaveTerminal: true);
        ProgressPercent = null;
        Warning = null;
        ErrorCategory = null;
        ErrorText = null;
        OutputSizeBytes = null;
        OutputPath = null;
        OutputPublished = false;
    }

    /// <summary>
    /// Übernimmt einen Fortschritt. <see cref="IProgress{T}"/> stellt asynchron zu, daher kann ein
    /// Bericht nach dem Endergebnis eintreffen; ein terminaler Zustand wird nie wieder verlassen
    /// (Abschnitt 6.2).
    /// </summary>
    public void ApplyProgress(CompressionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (IsTerminal)
        {
            return;
        }

        Status = progress.Status;
        ProgressPercent = progress.Percent;
    }

    /// <summary>Übernimmt genau das, was der Dienst gemeldet hat — ohne Beschönigung.</summary>
    public void ApplyOutcome(CompressionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        outcome.Validate();

        Status = outcome.Status;
        ProgressPercent = null;
        Warning = outcome.WarningText;
        ErrorCategory = outcome.ErrorCategory;
        ErrorText = outcome.ErrorText;
        OutputSizeBytes = outcome.OutputSizeBytes;
        OutputPath = outcome.OutputPath;
        OutputPublished = outcome.OutputPublished;
        JobId = outcome.JobId;
    }

    public void PrepareRetry()
    {
        if (Status is not (JobStatus.Failed or JobStatus.Canceled))
        {
            return;
        }

        PredecessorJobId = JobId;
        ResetForRun();
    }
}
