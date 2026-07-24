using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia.Media.Imaging;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.ViewModels;

/// <summary>
/// Vorher-Nachher-Vergleich. Vergleichbar ist jede Datei der Warteschlange: liegt bereits eine
/// validierte, veröffentlichte Ausgabe vor, wird genau diese gezeigt; sonst wird das Ergebnis mit
/// den aktuellen Einstellungen im Speicher erzeugt, ohne dass eine Datei entsteht (Abschnitt 11).
/// Die Vorschau entsteht über denselben nativen Kodierpfad wie das spätere Schreiben, damit
/// Orientierung, Farbprofil und Qualität identisch behandelt werden; sie ist auf
/// <see cref="MaxPreviewEdge"/> begrenzt, damit große Bilder den Speicher nicht sprengen.
/// </summary>
public sealed class CompareViewModel : ObservableObject
{
    /// <summary>
    /// Obergrenze der längeren Kante einer Vorschau in Pixeln. Sie entspricht der Grenze der
    /// nativen ABI: übliche Kameraauflösungen kommen dadurch unverkleinert an, damit eine
    /// Darstellung mit echten Bildpunkten möglich ist. Zoom und Schwenk bleiben reine
    /// Transformationen des geladenen Bildes — ein Nachladen je Schwenkschritt hieße, die Datei
    /// jedes Mal neu zu dekodieren.
    /// </summary>
    public const int MaxPreviewEdge = 8192;

    /// <summary>Größte Vergrößerung über die Originalauflösung hinaus.</summary>
    public const double MaxScale = 4.0;

    private readonly IPreviewRenderer? previewRenderer;
    private readonly SettingsViewModel? settings;
    private CancellationTokenSource? previewCancellation;
    private long? livePreviewSizeBytes;

    private QueueItemViewModel? selected;
    private double dividerFraction = 0.5;
    private Bitmap? originalPreview;
    private Bitmap? compressedPreview;
    private bool isPreviewLoading;
    private string? previewError;
    private double scale = 1;
    private double viewportWidth;
    private double viewportHeight;
    private int sourceWidth;
    private int sourceHeight;
    private bool isPreviewDownscaled;
    private double panX;
    private double panY;

    public CompareViewModel(
        IPreviewRenderer? previewRenderer = null,
        SettingsViewModel? settings = null)
    {
        this.previewRenderer = previewRenderer;
        this.settings = settings;
        if (settings is not null)
        {
            // Eine Einstellungsaenderung macht die Probekompression ungueltig.
            settings.PropertyChanged += (_, _) => ReloadIfLive();
        }

        ZoomInCommand = new RelayCommand(() => Scale *= 1.5);
        ZoomOutCommand = new RelayCommand(() => Scale /= 1.5);
        ActualSizeCommand = new RelayCommand(() => Scale = 1);
        ResetViewCommand = new RelayCommand(ResetView);
    }

    public ObservableCollection<QueueItemViewModel> Candidates { get; } = [];

    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand ActualSizeCommand { get; }
    public RelayCommand ResetViewCommand { get; }

    public QueueItemViewModel? Selected
    {
        get => selected;
        set
        {
            if (SetProperty(ref selected, value))
            {
                Raise(nameof(HasSelection));
                RaiseComparison();
                ResetView();
                _ = LoadPreviewsAsync();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>Position des Vergleichsreglers, 0..1.</summary>
    public double DividerFraction
    {
        get => dividerFraction;
        set => SetProperty(ref dividerFraction, Math.Clamp(value, 0, 1));
    }

    /// <summary>Original des ausgewählten Jobs; <c>null</c>, solange keine Vorschau vorliegt.</summary>
    public Bitmap? OriginalPreview
    {
        get => originalPreview;
        private set
        {
            var previous = originalPreview;
            if (SetProperty(ref originalPreview, value))
            {
                previous?.Dispose();
                Raise(nameof(HasPreview));
            }
        }
    }

    public Bitmap? CompressedPreview
    {
        get => compressedPreview;
        private set
        {
            var previous = compressedPreview;
            if (SetProperty(ref compressedPreview, value))
            {
                previous?.Dispose();
                Raise(nameof(HasPreview));
            }
        }
    }

    public bool HasPreview => OriginalPreview is not null && CompressedPreview is not null;

    public bool IsPreviewLoading
    {
        get => isPreviewLoading;
        private set => SetProperty(ref isPreviewLoading, value);
    }

    /// <summary>Konkrete Ursache, wenn keine Vorschau erzeugt werden konnte.</summary>
    public string? PreviewError
    {
        get => previewError;
        private set
        {
            if (SetProperty(ref previewError, value))
            {
                Raise(nameof(HasPreviewError));
            }
        }
    }

    public bool HasPreviewError => !string.IsNullOrEmpty(PreviewError);

    /// <summary>
    /// Anzeigemaßstab beider Seiten, bezogen auf die Bildpunkte des Originals: <c>1</c> bedeutet,
    /// dass ein Bildpunkt des Originals einem Bildpunkt auf dem Schirm entspricht. Der Wert ist
    /// absolut und nicht relativ zur Einpassung — sonst behauptete die Anzeige bei einem 5400
    /// Punkte breiten Bild in einem 1200 Punkte breiten Fenster „100 %“.
    /// </summary>
    public double Scale
    {
        get => scale;
        set
        {
            // Einpassung und Originalgröße müssen immer erreichbar bleiben, auch wenn ein
            // kleines Bild in einer großen Fläche über MaxScale hinaus eingepasst wird.
            var lower = Math.Min(FitScale, 1);
            var upper = Math.Max(FitScale, MaxScale);
            if (SetProperty(ref scale, Math.Clamp(value, lower, upper)))
            {
                ClampPan();
                Raise(nameof(RenderScale));
                Raise(nameof(ZoomLabel));
            }
        }
    }

    /// <summary>Maßstab, bei dem das Bild vollständig in die Fläche passt.</summary>
    public double FitScale => sourceWidth <= 0 || sourceHeight <= 0
        || viewportWidth <= 0 || viewportHeight <= 0
        ? 1
        : Math.Min(viewportWidth / sourceWidth, viewportHeight / sourceHeight);

    /// <summary>
    /// Faktor für die Ansicht. Das Bild wird bereits eingepasst dargestellt, die Transformation
    /// muss also nur den Unterschied zwischen Einpassung und gewünschtem Maßstab ausgleichen.
    /// </summary>
    public double RenderScale => FitScale <= 0 ? 1 : Scale / FitScale;

    /// <summary>Anzeigemaßstab in Prozent; Formatierung über die aktive Kultur (Abschnitt 11.2).</summary>
    public string ZoomLabel => Localizer.Instance.Format("Cmp_ZoomValue", Scale);

    /// <summary>
    /// Meldet, dass die Vorschau kleiner als das Original geladen wurde. Dann zeigt auch
    /// <c>100 %</c> hochgerechnete Bildpunkte statt echter.
    /// </summary>
    public bool IsPreviewDownscaled
    {
        get => isPreviewDownscaled;
        private set => SetProperty(ref isPreviewDownscaled, value);
    }

    /// <summary>
    /// Meldet die Größe der Vergleichsfläche. Ohne sie lässt sich der Maßstab nicht bestimmen;
    /// die Ansicht kennt sie, das ViewModel nicht.
    /// </summary>
    public void ApplyViewport(double width, double height)
    {
        if (width <= 0 || height <= 0
            || (Math.Abs(viewportWidth - width) < 0.5 && Math.Abs(viewportHeight - height) < 0.5))
        {
            return;
        }

        var wasFitted = Math.Abs(Scale - FitScale) < 1e-9;
        viewportWidth = width;
        viewportHeight = height;
        Raise(nameof(FitScale));

        // Eine eingepasste Ansicht bleibt eingepasst, wenn sich die Fläche ändert.
        Scale = wasFitted ? FitScale : Scale;
        Raise(nameof(RenderScale));
        Raise(nameof(ZoomLabel));
    }

    public double PanX
    {
        get => panX;
        private set => SetProperty(ref panX, value);
    }

    public double PanY
    {
        get => panY;
        private set => SetProperty(ref panY, value);
    }

    /// <summary>Verschiebt beide Seiten gemeinsam; ohne Zoom gibt es nichts zu schwenken.</summary>
    public void Pan(double deltaX, double deltaY)
    {
        PanX += deltaX;
        PanY += deltaY;
        ClampPan();
    }

    public void ResetView()
    {
        Scale = FitScale;
        PanX = 0;
        PanY = 0;
    }

    /// <summary>
    /// Übernimmt die Warteschlange als Kandidatenliste. Vergleichbar ist jede eingereihte Datei,
    /// nicht erst ein fertiges Ergebnis: sonst müsste man erst konvertieren, um zu sehen, ob die
    /// Einstellungen taugen.
    /// </summary>
    public void AttachQueue(ObservableCollection<QueueItemViewModel> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        foreach (var item in queue)
        {
            Candidates.Add(item);
        }

        queue.CollectionChanged += OnQueueChanged;
        Selected ??= Candidates.FirstOrDefault();
        Raise(nameof(HasCandidates));
    }

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in e.OldItems?.OfType<QueueItemViewModel>() ?? [])
        {
            Candidates.Remove(removed);
            if (ReferenceEquals(Selected, removed))
            {
                Selected = Candidates.FirstOrDefault();
            }
        }

        foreach (var added in e.NewItems?.OfType<QueueItemViewModel>() ?? [])
        {
            Candidates.Add(added);
        }

        Selected ??= Candidates.FirstOrDefault();
        Raise(nameof(HasCandidates));
    }

    /// <summary>
    /// Erneuert eine Probekompression, wenn sich die Einstellungen geändert haben. Ein bereits
    /// veröffentlichtes Ergebnis bleibt unberührt: es zeigt die tatsächlich geschriebene Datei
    /// und nicht das, was aktuelle Einstellungen ergäben.
    /// </summary>
    public void ReloadIfLive()
    {
        if (Selected is { CanCompare: false })
        {
            _ = LoadPreviewsAsync();
        }
    }

    /// <summary>
    /// Lädt beide Vorschauen. Ein Wechsel der Auswahl bricht den laufenden Ladevorgang ab, damit
    /// ein verspätetes Ergebnis nicht die neue Auswahl überschreibt.
    /// </summary>
    private async Task LoadPreviewsAsync()
    {
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = null;

        OriginalPreview = null;
        CompressedPreview = null;
        PreviewError = null;

        var item = Selected;
        if (item is null || previewRenderer is null)
        {
            return;
        }

        livePreviewSizeBytes = null;
        Raise(nameof(SizeSummary));
        RaiseComparison();

        var cancellation = new CancellationTokenSource();
        previewCancellation = cancellation;
        IsPreviewLoading = true;
        try
        {
            var original = await RenderAsync(item.InputPath, item, cancellation.Token)
                .ConfigureAwait(true);

            // Ein veröffentlichtes Ergebnis wird gezeigt, wie es auf dem Datenträger liegt.
            // Alles andere wird für die Vorschau im Speicher komprimiert.
            var compressed = item.OutputPath is string outputPath && item.CanCompare
                ? await RenderAsync(outputPath, item, cancellation.Token).ConfigureAwait(true)
                : await RenderLiveAsync(item, cancellation.Token).ConfigureAwait(true);

            if (cancellation.IsCancellationRequested || !ReferenceEquals(Selected, item))
            {
                original?.Dispose();
                compressed?.Dispose();
                return;
            }

            OriginalPreview = original;
            CompressedPreview = compressed;
            Raise(nameof(SizeSummary));
            RaiseComparison();
            ResetView();
        }
        catch (OperationCanceledException)
        {
            // Ein Auswahlwechsel ist kein Fehler.
        }
        catch (Exception exception)
        {
            PreviewError = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(previewCancellation, cancellation))
            {
                IsPreviewLoading = false;
            }
        }
    }

    /// <summary>
    /// Komprimiert die Auswahl mit den aktuellen Einstellungen im Speicher. Es entsteht keine
    /// Datei; die ermittelte Grösse dient nur der Anzeige.
    /// </summary>
    private async Task<Bitmap?> RenderLiveAsync(
        QueueItemViewModel item,
        CancellationToken cancellationToken)
    {
        if (settings?.TryBuildEngineSettings() is not JpegliSettings jpegli)
        {
            PreviewError = Localizer.Instance["Cmp_PreviewUnsupportedEngine"];
            return null;
        }

        var result = await previewRenderer!
            .RenderEncodedPreviewAsync(
                item.InputPath,
                MaxPreviewEdge,
                jpegli,
                settings.AlphaBackground,
                settings.ExifPolicy,
                settings.ColorProfilePolicy,
                cancellationToken)
            .ConfigureAwait(true);

        if (result.Image is not PreviewImage image)
        {
            PreviewError = result.ErrorText ?? Localizer.Instance["Cmp_PreviewFailed"];
            return null;
        }

        livePreviewSizeBytes = result.EncodedSizeBytes;
        return ApplySource(image);
    }

    /// <summary>
    /// Grössenvergleich der Auswahl. Für eine Probekompression stammt die Ergebnisgrösse aus dem
    /// Speicher, nicht aus einer Datei; sie ist als Schätzung gekennzeichnet.
    /// </summary>
    public string? SizeSummary
    {
        get
        {
            if (Selected is not QueueItemViewModel item)
            {
                return null;
            }

            return livePreviewSizeBytes is long live
                ? Localizer.Instance.Format(
                    "Cmp_EstimatedSize",
                    ByteFormat.Describe(item.InputSizeBytes),
                    ByteFormat.Describe(live),
                    ByteFormat.DescribeSavings(item.InputSizeBytes, live))
                : item.SizeSummary;
        }
    }

    // --- Vorher/Nachher-Tabelle (Issue #4). Nur Grösse, Format, Maße und Einsparung; die
    // Vorlage nennt bewusst keine Qualitätskennzahlen (Butteraugli/SSIM) — Abschnitt 11.1. ---

    /// <summary>Eine Auswahl liegt vor; die Tabelle hat Inhalt.</summary>
    public bool HasComparison => Selected is not null;

    /// <summary>Die Ausgabegrösse ist eine Probekompression im Speicher, keine veröffentlichte Datei.</summary>
    public bool IsEstimate => Selected is { CanCompare: false };

    private long? EffectiveOutputSizeBytes => Selected is { } item
        ? item.CanCompare ? item.OutputSizeBytes : livePreviewSizeBytes
        : null;

    /// <summary>Eingabeformat aus der Endung; ein stabiler Bezeichner, unübersetzt (Abschnitt 4.3).</summary>
    public string? BeforeFormat => Selected is { } item
        ? Path.GetExtension(item.InputPath).ToLowerInvariant() == ".png" ? "PNG" : "JPEG"
        : null;

    /// <summary>Die Ausgabe ist immer JPEG (Abschnitt 8.1).</summary>
    public string? AfterFormat => Selected is null ? null : "JPEG";

    public string? BeforeSize => Selected is { } item ? ByteFormat.Describe(item.InputSizeBytes) : null;

    public string? AfterSize =>
        EffectiveOutputSizeBytes is long size ? ByteFormat.Describe(size) : null;

    public string? SavingsText => Selected is { } item && EffectiveOutputSizeBytes is long size
        ? ByteFormat.DescribeSavings(item.InputSizeBytes, size)
        : null;

    /// <summary>Kompakte Grössendelta-Zeile der Infoleiste (UI-Doc 06); Pfeil bleibt aus dem XAML.</summary>
    public string? DeltaText => BeforeSize is { } before && AfterSize is { } after
        ? $"{before} → {after} · {SavingsText}"
        : null;

    /// <summary>Engine des ausgewählten Ergebnisses für die Infoleiste.</summary>
    public string? EngineText => Selected?.EngineLabel;

    /// <summary>Maße des aufrecht gedrehten Originals; Ausgabe behält sie (kein Resize, Abschnitt 3.2).</summary>
    public string? Dimensions => sourceWidth > 0 && sourceHeight > 0
        ? string.Format(
            CultureInfo.CurrentCulture,
            "{0} × {1}",
            sourceWidth,
            sourceHeight)
        : null;

    private void RaiseComparison()
    {
        Raise(nameof(HasComparison));
        Raise(nameof(IsEstimate));
        Raise(nameof(BeforeFormat));
        Raise(nameof(AfterFormat));
        Raise(nameof(BeforeSize));
        Raise(nameof(AfterSize));
        Raise(nameof(SavingsText));
        Raise(nameof(DeltaText));
        Raise(nameof(EngineText));
        Raise(nameof(Dimensions));
    }

    private Bitmap ApplySource(PreviewImage image)
    {
        // Der Maßstab bezieht sich auf das Original, nicht auf die geladene Vorschau.
        sourceWidth = image.SourceWidth;
        sourceHeight = image.SourceHeight;
        IsPreviewDownscaled = image.ScaleFromSource < 1;
        Raise(nameof(FitScale));
        return PreviewBitmap.ToBitmap(image);
    }

    private async Task<Bitmap?> RenderAsync(
        string path,
        QueueItemViewModel item,
        CancellationToken cancellationToken)
    {
        var result = await previewRenderer!
            .RenderPreviewAsync(path, MaxPreviewEdge, item.AlphaBackground, cancellationToken)
            .ConfigureAwait(true);

        if (result.Image is not PreviewImage image)
        {
            PreviewError = result.ErrorText ?? Localizer.Instance["Cmp_PreviewFailed"];
            return null;
        }

        return ApplySource(image);
    }

    /// <summary>
    /// Hält den Bildausschnitt innerhalb des Bildes: solange alles in die Fläche passt, gibt es
    /// nichts zu schwenken; darüber wächst der zulässige Bereich mit dem überstehenden Anteil.
    /// </summary>
    private void ClampPan()
    {
        var overflow = Math.Max(0, RenderScale - 1) / 2;
        PanX = Math.Clamp(PanX, -viewportWidth * overflow, viewportWidth * overflow);
        PanY = Math.Clamp(PanY, -viewportHeight * overflow, viewportHeight * overflow);
    }
}
