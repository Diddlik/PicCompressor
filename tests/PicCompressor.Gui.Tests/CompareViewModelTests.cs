using System.Collections.ObjectModel;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Tests;

/// <summary>
/// Prüft die Vergleichslogik ohne Avalonia-Plattform. Die Umwandlung der Vorschaupixel in ein
/// Bitmap braucht einen laufenden Renderer und wird über die Interop-Tests des Wrappers
/// abgedeckt; hier bleibt der Renderer daher bei Fehler- und Zustandspfaden.
/// </summary>
public sealed class CompareViewModelTests
{
    [Fact]
    public void The_scale_stays_within_the_supported_range()
    {
        var compare = Fitted(4000, 2000, 800, 400);

        compare.Scale = 100;
        Assert.Equal(CompareViewModel.MaxScale, compare.Scale);

        // Weiter herausgezoomt als die Einpassung ergibt keinen Sinn.
        compare.Scale = 0.001;
        Assert.Equal(compare.FitScale, compare.Scale);
    }

    [Fact]
    public void The_scale_refers_to_the_original_pixels_not_to_the_fitted_view()
    {
        // 4000 Punkte breites Bild in einer 800 Punkte breiten Flaeche: ein Fuenftel.
        var compare = Fitted(4000, 2000, 800, 400);

        Assert.Equal(0.2, compare.FitScale, 6);
        Assert.Equal(0.2, compare.Scale, 6);
        Assert.Contains("20", compare.ZoomLabel, StringComparison.Ordinal);
        // Eingepasst zeigt die Ansicht das Bild unveraendert; erst darueber wird vergroessert.
        Assert.Equal(1, compare.RenderScale, 6);
    }

    [Fact]
    public void Actual_size_shows_one_screen_pixel_per_image_pixel()
    {
        var compare = Fitted(4000, 2000, 800, 400);

        compare.ActualSizeCommand.Execute(null);

        Assert.Equal(1, compare.Scale, 6);
        Assert.Contains("100", compare.ZoomLabel, StringComparison.Ordinal);
        // Bei 100 Prozent ist das Bild fuenfmal so gross wie die eingepasste Ansicht.
        Assert.Equal(5, compare.RenderScale, 6);
    }

    [Fact]
    public void A_smaller_surface_keeps_a_fitted_view_fitted()
    {
        var compare = Fitted(4000, 2000, 800, 400);

        compare.ApplyViewport(400, 200);

        Assert.Equal(0.1, compare.FitScale, 6);
        Assert.Equal(0.1, compare.Scale, 6);
    }

    [Fact]
    public void A_reduced_preview_is_reported_because_100_percent_is_then_interpolated()
    {
        var compare = Fitted(4000, 2000, 800, 400);

        Assert.True(compare.IsPreviewDownscaled);
    }

    [Theory]
    [InlineData(1.0, "100")]
    [InlineData(2.0, "200")]
    [InlineData(4.0, "400")]
    public void The_zoom_level_is_shown_as_a_percentage(double scale, string expected)
    {
        var compare = Fitted(4000, 2000, 800, 400);
        compare.Scale = scale;

        Assert.Contains(expected, compare.ZoomLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void The_zoom_label_follows_the_active_language()
    {
        var previous = Localizer.Instance.Language;
        try
        {
            var compare = Fitted(4000, 2000, 800, 400);
            compare.Scale = 2;

            Localizer.Instance.Language = AppLanguage.German;
            var german = compare.ZoomLabel;
            Localizer.Instance.Language = AppLanguage.English;

            // Deutsch trennt Zahl und Prozentzeichen, Englisch nicht; beide über die Kultur.
            Assert.NotEqual(german, compare.ZoomLabel);
        }
        finally
        {
            Localizer.Instance.Language = previous;
        }
    }

    [Fact]
    public void Panning_is_impossible_without_zoom()
    {
        var compare = Fitted(4000, 2000, 800, 400);

        compare.Pan(200, -80);

        Assert.Equal(0, compare.PanX);
        Assert.Equal(0, compare.PanY);
    }

    [Fact]
    public void Resetting_the_view_restores_zoom_and_pan()
    {
        var compare = Fitted(4000, 2000, 800, 400);
        compare.Scale = 1;
        compare.Pan(50, 60);
        Assert.NotEqual(0, compare.PanX);

        compare.ResetViewCommand.Execute(null);

        Assert.Equal(compare.FitScale, compare.Scale);
        Assert.Equal(0, compare.PanX);
        Assert.Equal(0, compare.PanY);
    }

    [Fact]
    public void Zooming_out_pulls_the_pan_back_into_range()
    {
        var compare = Fitted(4000, 2000, 800, 400);
        compare.Scale = CompareViewModel.MaxScale;
        compare.Pan(100_000, 0);
        var panned = compare.PanX;
        Assert.True(panned > 0);

        compare.Scale = 1;

        Assert.True(compare.PanX < panned);
    }

    [Fact]
    public void Selecting_a_result_resets_the_view()
    {
        var compare = Fitted(4000, 2000, 800, 400);
        compare.Scale = 1;
        compare.Pan(40, 40);

        // Ein Wechsel der Auswahl, nicht nur ein weiterer Kandidat.
        compare.Selected = Published("a.jpg", "a_compressed.jpg");

        Assert.Equal(compare.FitScale, compare.Scale);
        Assert.Equal(0, compare.PanX);
    }

    [Fact]
    public void A_file_that_was_never_converted_is_compressed_in_memory_for_the_preview()
    {
        var renderer = new FakePreviewRenderer(PreviewResult.Failed("stop"));
        var queued = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 1000);

        WithQueue(renderer, queued, new SettingsViewModel());

        // Ohne veroeffentlichte Ausgabe darf keine Datei gelesen, sondern muss kodiert werden.
        Assert.Equal(1, renderer.EncodedCalls);
        Assert.Equal(0, renderer.OutputPathCalls);
    }

    [Fact]
    public void A_published_result_is_shown_as_written_instead_of_being_re_encoded()
    {
        var renderer = new FakePreviewRenderer(PreviewResult.Failed("stop"));
        var published = Published("a.jpg", "a_compressed.jpg");

        WithQueue(renderer, published, new SettingsViewModel());

        Assert.Equal(0, renderer.EncodedCalls);
        Assert.Equal(1, renderer.OutputPathCalls);
    }

    [Fact]
    public void Changing_a_setting_renews_the_trial_compression()
    {
        var renderer = new FakePreviewRenderer(PreviewResult.Failed("stop"));
        var settings = new SettingsViewModel();
        WithQueue(renderer, new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 1000), settings);
        var before = renderer.EncodedCalls;

        settings.Quality = settings.Quality == 90 ? 60 : 90;

        Assert.True(
            renderer.EncodedCalls > before,
            $"expected a new trial compression, calls stayed at {renderer.EncodedCalls}");
    }

    [Fact]
    public async Task A_failed_preview_reports_the_native_reason()
    {
        var renderer = new FakePreviewRenderer(PreviewResult.Failed("decoder said no"));
        var compare = WithQueue(renderer, Published("a.jpg", "a_compressed.jpg"));

        await renderer.Completed;

        Assert.Equal("decoder said no", compare.PreviewError);
        Assert.False(compare.HasPreview);
    }

    [Fact]
    public void Without_a_renderer_the_view_stays_empty_instead_of_faking_a_preview()
    {
        var compare = WithQueue(null, Published("a.jpg", "a_compressed.jpg"));

        Assert.False(compare.HasPreview);
        Assert.False(compare.IsPreviewLoading);
        Assert.Null(compare.PreviewError);
    }

    [Fact]
    public void The_alpha_background_of_the_run_reaches_the_preview()
    {
        var item = Published("a.png", "a_compressed.jpg");
        item.AlphaBackground = new RgbColor(10, 20, 30);
        var renderer = new FakePreviewRenderer(PreviewResult.Failed("stop"));

        WithQueue(renderer, item);

        Assert.Equal(new RgbColor(10, 20, 30), renderer.LastBackground);
    }

    /// <summary>
    /// ViewModel mit bekannten Bild- und Flaechenmassen. Der Massstab braucht beides; ohne
    /// Vorschau kennt das ViewModel die Bildgroesse nicht.
    /// </summary>
    private static CompareViewModel Fitted(
        int sourceWidth,
        int sourceHeight,
        double viewportWidth,
        double viewportHeight)
    {
        // Bewusst eine verkleinerte Vorschau: der Maßstab bezieht sich auf das Original, nicht
        // auf die geladenen Vorschaupixel. Das hält den Test zugleich klein.
        var compare = WithQueue(
            new FakePreviewRenderer(
                new PreviewResult(
                    new PreviewImage(
                        sourceWidth / 10,
                        sourceHeight / 10,
                        new byte[sourceWidth / 10 * (sourceHeight / 10) * 3],
                        sourceWidth,
                        sourceHeight),
                    null)),
            Published("source.jpg", "source_compressed.jpg"));
        compare.ApplyViewport(viewportWidth, viewportHeight);
        return compare;
    }


    /// <summary>
    /// Vergleich mit angehaengter Warteschlange. Kandidat ist jede eingereihte Datei, nicht erst
    /// ein fertiges Ergebnis.
    /// </summary>
    private static CompareViewModel WithQueue(
        IPreviewRenderer? renderer,
        params QueueItemViewModel[] items) => WithQueue(renderer, items, null);

    private static CompareViewModel WithQueue(
        IPreviewRenderer? renderer,
        QueueItemViewModel item,
        SettingsViewModel settings) => WithQueue(renderer, [item], settings);

    private static CompareViewModel WithQueue(
        IPreviewRenderer? renderer,
        QueueItemViewModel[] items,
        SettingsViewModel? settings)
    {
        var compare = new CompareViewModel(renderer, settings);
        compare.AttachQueue(new ObservableCollection<QueueItemViewModel>(items));
        return compare;
    }

    private static QueueItemViewModel Published(string input, string output)
    {
        var item = new QueueItemViewModel(input, EngineIds.Jpegli, 100);
        item.ApplyOutcome(
            new CompressionOutcome(
                JobStatus.Succeeded, input, output, 100, 50, true, null, null, null));
        return item;
    }

    private sealed class FakePreviewRenderer(PreviewResult result) : IPreviewRenderer
    {
        private readonly TaskCompletionSource completed = new();

        public Task Completed => completed.Task;

        public RgbColor? LastBackground { get; private set; }

        public long EncodedSizeBytes { get; init; }

        public int EncodedCalls { get; private set; }

        /// <summary>Aufrufe, die eine bereits geschriebene Ausgabedatei lesen.</summary>
        public int OutputPathCalls { get; private set; }

        public Task<PreviewResult> RenderPreviewAsync(
            string inputPath,
            int maxEdge,
            RgbColor alphaBackground,
            CancellationToken cancellationToken)
        {
            LastBackground = alphaBackground;
            if (inputPath.Contains("_compressed", StringComparison.Ordinal))
            {
                OutputPathCalls++;
            }

            completed.TrySetResult();
            return Task.FromResult(result);
        }
        public Task<EncodedPreviewResult> RenderEncodedPreviewAsync(
            string inputPath,
            int maxEdge,
            JpegliSettings settings,
            RgbColor alphaBackground,
            ExifPolicy exifPolicy,
            ColorProfilePolicy colorProfilePolicy,
            CancellationToken cancellationToken)
        {
            EncodedCalls++;
            return Task.FromResult(
                new EncodedPreviewResult(result.Image, EncodedSizeBytes, result.ErrorText));
        }
    }
}
