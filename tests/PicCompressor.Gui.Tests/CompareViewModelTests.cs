using PicCompressor.Application;
using PicCompressor.Domain;
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
    public void Zoom_stays_within_the_supported_range()
    {
        var compare = new CompareViewModel();

        compare.Zoom = 100;
        Assert.Equal(CompareViewModel.MaxZoom, compare.Zoom);

        compare.Zoom = 0.1;
        Assert.Equal(CompareViewModel.MinZoom, compare.Zoom);
    }

    [Fact]
    public void Panning_is_impossible_without_zoom()
    {
        var compare = new CompareViewModel();

        compare.Pan(200, -80);

        Assert.Equal(0, compare.PanX);
        Assert.Equal(0, compare.PanY);
    }

    [Fact]
    public void Resetting_the_view_restores_zoom_and_pan()
    {
        var compare = new CompareViewModel { Zoom = 4 };
        compare.Pan(50, 60);
        Assert.NotEqual(0, compare.PanX);

        compare.ResetViewCommand.Execute(null);

        Assert.Equal(CompareViewModel.MinZoom, compare.Zoom);
        Assert.Equal(0, compare.PanX);
        Assert.Equal(0, compare.PanY);
    }

    [Fact]
    public void Zooming_out_pulls_the_pan_back_into_range()
    {
        var compare = new CompareViewModel { Zoom = CompareViewModel.MaxZoom };
        compare.Pan(100_000, 0);
        var panned = compare.PanX;
        Assert.True(panned > 0);

        compare.Zoom = 2;

        Assert.True(compare.PanX < panned);
    }

    [Fact]
    public void Selecting_a_result_resets_the_view()
    {
        var compare = new CompareViewModel { Zoom = 3 };
        compare.Pan(40, 40);

        compare.Offer(Published("a.jpg", "a_compressed.jpg"));

        Assert.Equal(CompareViewModel.MinZoom, compare.Zoom);
        Assert.Equal(0, compare.PanX);
    }

    [Fact]
    public async Task A_failed_preview_reports_the_native_reason()
    {
        var renderer = new FakePreviewRenderer(PreviewResult.Failed("decoder said no"));
        var compare = new CompareViewModel(renderer);

        compare.Offer(Published("a.jpg", "a_compressed.jpg"));
        await renderer.Completed;

        Assert.Equal("decoder said no", compare.PreviewError);
        Assert.False(compare.HasPreview);
    }

    [Fact]
    public void Without_a_renderer_the_view_stays_empty_instead_of_faking_a_preview()
    {
        var compare = new CompareViewModel();

        compare.Offer(Published("a.jpg", "a_compressed.jpg"));

        Assert.False(compare.HasPreview);
        Assert.False(compare.IsPreviewLoading);
        Assert.Null(compare.PreviewError);
    }

    [Fact]
    public void The_alpha_background_of_the_run_reaches_the_preview()
    {
        var renderer = new FakePreviewRenderer(PreviewResult.Failed("stop"));
        var compare = new CompareViewModel(renderer);
        var item = Published("a.png", "a_compressed.jpg");
        item.AlphaBackground = new RgbColor(10, 20, 30);

        compare.Offer(item);

        Assert.Equal(new RgbColor(10, 20, 30), renderer.LastBackground);
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

        public Task<PreviewResult> RenderPreviewAsync(
            string inputPath,
            int maxEdge,
            RgbColor alphaBackground,
            CancellationToken cancellationToken)
        {
            LastBackground = alphaBackground;
            completed.TrySetResult();
            return Task.FromResult(result);
        }
    }
}
