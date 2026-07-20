using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PicCompressor.Application;
using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.ViewModels;

/// <summary>
/// Vorher-Nachher-Vergleich. Kandidaten sind ausschließlich Jobs mit validierter, veröffentlichter
/// Ausgabe (Abschnitt 11). Die Vorschau entsteht über denselben nativen Dekodierpfad wie das
/// Encoding, damit Orientierung und Farbprofil identisch behandelt werden; sie ist auf
/// <see cref="MaxPreviewEdge"/> begrenzt, damit große Bilder den Speicher nicht sprengen.
/// </summary>
public sealed class CompareViewModel : ObservableObject
{
    /// <summary>Obergrenze der längeren Kante einer Vorschau in Pixeln.</summary>
    public const int MaxPreviewEdge = 1600;

    public const double MinZoom = 1.0;
    public const double MaxZoom = 8.0;

    private readonly IPreviewRenderer? previewRenderer;
    private CancellationTokenSource? previewCancellation;

    private QueueItemViewModel? selected;
    private double dividerFraction = 0.5;
    private Bitmap? originalPreview;
    private Bitmap? compressedPreview;
    private bool isPreviewLoading;
    private string? previewError;
    private double zoom = MinZoom;
    private double panX;
    private double panY;

    public CompareViewModel(IPreviewRenderer? previewRenderer = null)
    {
        this.previewRenderer = previewRenderer;
        ZoomInCommand = new RelayCommand(() => Zoom *= 1.5);
        ZoomOutCommand = new RelayCommand(() => Zoom /= 1.5);
        ResetViewCommand = new RelayCommand(ResetView);
    }

    public ObservableCollection<QueueItemViewModel> Candidates { get; } = [];

    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand ResetViewCommand { get; }

    public QueueItemViewModel? Selected
    {
        get => selected;
        set
        {
            if (SetProperty(ref selected, value))
            {
                Raise(nameof(HasSelection));
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

    /// <summary>Gemeinsamer Zoomfaktor beider Seiten; der Vergleich bleibt damit synchron.</summary>
    public double Zoom
    {
        get => zoom;
        set
        {
            if (SetProperty(ref zoom, Math.Clamp(value, MinZoom, MaxZoom)))
            {
                ClampPan();
                Raise(nameof(ZoomLabel));
            }
        }
    }

    public string ZoomLabel => Localizer.Instance.Format("Cmp_ZoomValue", Zoom);

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
        Zoom = MinZoom;
        PanX = 0;
        PanY = 0;
    }

    /// <summary>
    /// Übernimmt einen abgeschlossenen Job nur, wenn seine Ausgabe validiert und veröffentlicht
    /// wurde. Alles andere ist nicht vergleichbar.
    /// </summary>
    public void Offer(QueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanCompare || Candidates.Contains(item))
        {
            return;
        }

        Candidates.Insert(0, item);
        Selected ??= item;
        Raise(nameof(HasCandidates));
    }

    public void Remove(QueueItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Candidates.Remove(item))
        {
            return;
        }

        if (ReferenceEquals(Selected, item))
        {
            Selected = Candidates.FirstOrDefault();
        }

        Raise(nameof(HasCandidates));
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
        if (item?.OutputPath is not string outputPath || previewRenderer is null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        previewCancellation = cancellation;
        IsPreviewLoading = true;
        try
        {
            var original = await RenderAsync(item.InputPath, item, cancellation.Token)
                .ConfigureAwait(true);
            var compressed = await RenderAsync(outputPath, item, cancellation.Token)
                .ConfigureAwait(true);

            if (cancellation.IsCancellationRequested || !ReferenceEquals(Selected, item))
            {
                original?.Dispose();
                compressed?.Dispose();
                return;
            }

            OriginalPreview = original;
            CompressedPreview = compressed;
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

        return ToBitmap(image);
    }

    /// <summary>
    /// Wandelt das dicht gepackte RGB des Wrappers in das von Avalonia erwartete BGRA um.
    /// </summary>
    private static Bitmap ToBitmap(PreviewImage image)
    {
        var rowBytes = image.Width * 4;
        var bgra = new byte[rowBytes * image.Height];
        for (var pixel = 0; pixel < image.Width * image.Height; ++pixel)
        {
            bgra[(pixel * 4) + 0] = image.Rgb[(pixel * 3) + 2];
            bgra[(pixel * 4) + 1] = image.Rgb[(pixel * 3) + 1];
            bgra[(pixel * 4) + 2] = image.Rgb[pixel * 3];
            bgra[(pixel * 4) + 3] = 255;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(image.Width, image.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var buffer = bitmap.Lock();
        for (var y = 0; y < image.Height; ++y)
        {
            // Die Zeilen des Puffers dürfen breiter sein als die Bildzeile.
            Marshal.Copy(bgra, y * rowBytes, buffer.Address + (y * buffer.RowBytes), rowBytes);
        }

        return bitmap;
    }

    /// <summary>
    /// Hält den Bildausschnitt innerhalb des Bildes: bei Zoom 1 gibt es keinen Schwenk,
    /// darüber wächst der zulässige Bereich mit dem Zoomfaktor.
    /// </summary>
    private void ClampPan()
    {
        var limit = (Zoom - 1) * MaxPreviewEdge / 2;
        PanX = Math.Clamp(PanX, -limit, limit);
        PanY = Math.Clamp(PanY, -limit, limit);
    }
}
