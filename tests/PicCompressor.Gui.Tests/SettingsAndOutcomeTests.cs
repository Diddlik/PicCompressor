using PicCompressor.Domain;
using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void Defaults_follow_the_safe_product_defaults()
    {
        var settings = new SettingsViewModel();

        Assert.Equal(EngineIds.Jpegli, settings.EngineId);
        Assert.Equal(CollisionPolicy.Skip, settings.CollisionPolicy);
        Assert.Equal(LargerOutputPolicy.Discard, settings.LargerOutputPolicy);
        Assert.Equal(RgbColor.White, settings.AlphaBackground);
        Assert.Equal("_compressed", settings.Suffix);
    }

    [Fact]
    public void Guetzli_raises_the_quality_floor_and_jpegli_releases_it()
    {
        var settings = new SettingsViewModel { Quality = 10 };

        settings.IsGuetzli = true;
        Assert.Equal(EngineIds.GuetzliMinimumQuality, settings.MinQuality);
        Assert.True(settings.Quality >= EngineIds.GuetzliMinimumQuality);

        settings.IsJpegli = true;
        Assert.Equal(1, settings.MinQuality);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(250, 100)]
    public void Quality_is_clamped(int requested, int expected)
    {
        var settings = new SettingsViewModel { Quality = requested };

        Assert.Equal(expected, settings.Quality);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(7, 2)]
    public void Progressive_level_is_clamped_to_the_supported_range(int requested, int expected)
    {
        var settings = new SettingsViewModel { ProgressiveLevel = requested };

        Assert.Equal(expected, settings.ProgressiveLevel);
    }

    [Fact]
    public void Jpegli_settings_carry_the_selected_values()
    {
        var settings = new SettingsViewModel
        {
            Quality = 77,
            ProgressiveLevel = 1,
            Is440 = true
        };

        var built = Assert.IsType<JpegliSettings>(settings.TryBuildEngineSettings());
        Assert.Equal(77, built.Quality);
        Assert.Equal(1, built.ProgressiveLevel);
        Assert.Equal(JpegliChromaSubsampling.Subsampling440, built.ChromaSubsampling);
    }

    [Fact]
    public void Engines_without_a_domain_model_yield_no_settings()
    {
        var settings = new SettingsViewModel { IsGuetzli = true };

        Assert.Null(settings.TryBuildEngineSettings());
    }

    [Fact]
    public void Exif_and_colour_profile_are_independent()
    {
        var settings = new SettingsViewModel { ExifPrivate = true, ProfileSrgb = true };

        Assert.Equal(ExifPolicy.Private, settings.ExifPolicy);
        Assert.Equal(ColorProfilePolicy.Srgb, settings.ColorProfilePolicy);

        settings.ExifRemove = true;

        Assert.Equal(ColorProfilePolicy.Srgb, settings.ColorProfilePolicy);
    }
}

public sealed class CompressionOutcomeTests
{
    [Fact]
    public void Success_with_an_error_category_is_rejected()
    {
        var outcome = new CompressionOutcome(
            JobStatus.Succeeded, "a", "b", 10, 5, true, null,
            CompressionErrorCategory.EngineFailed, "x");

        Assert.Throws<InvalidOperationException>(outcome.Validate);
    }

    [Fact]
    public void Failure_without_a_category_is_rejected()
    {
        var outcome = new CompressionOutcome(
            JobStatus.Failed, "a", null, 10, null, false, null, null, null);

        Assert.Throws<InvalidOperationException>(outcome.Validate);
    }

    [Fact]
    public void Publishing_without_an_output_path_is_rejected()
    {
        var outcome = new CompressionOutcome(
            JobStatus.Succeeded, "a", null, 10, 5, true, null, null, null);

        Assert.Throws<InvalidOperationException>(outcome.Validate);
    }

    [Fact]
    public void A_failed_job_may_not_publish_output()
    {
        var outcome = new CompressionOutcome(
            JobStatus.Failed, "a", "b", 10, 5, true, null,
            CompressionErrorCategory.FileSystemError, "x");

        Assert.Throws<InvalidOperationException>(outcome.Validate);
    }

    [Fact]
    public void Discarded_but_successful_output_is_valid_and_not_comparable()
    {
        var outcome = new CompressionOutcome(
            JobStatus.Succeeded, "a", null, 10, 12, false, "not smaller", null, null);

        outcome.Validate();

        var item = new QueueItemViewModel("a", EngineIds.Jpegli, 10);
        item.ApplyOutcome(outcome);

        Assert.True(item.HasWarning);
        Assert.False(item.CanCompare);
    }
}

public sealed class QueueItemViewModelTests
{
    [Fact]
    public void Progress_stays_indeterminate_without_a_reliable_percentage()
    {
        var item = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 10)
        {
            Status = JobStatus.Encoding
        };

        Assert.True(item.IsIndeterminate);

        item.ProgressPercent = 40;

        Assert.False(item.IsIndeterminate);
    }

    [Fact]
    public void Terminal_jobs_are_never_indeterminate()
    {
        var item = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 10)
        {
            Status = JobStatus.Succeeded
        };

        Assert.False(item.IsIndeterminate);
        Assert.True(item.IsTerminal);
    }

    [Fact]
    public void Error_summary_keeps_the_stable_category_identifier()
    {
        var item = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 10);
        item.ApplyOutcome(
            CompressionOutcome.Failed(
                "a.jpg", 10, CompressionErrorCategory.OutputConflict, "target exists"));

        Assert.StartsWith("OutputConflict", item.ErrorSummary);
    }

    [Fact]
    public void Status_label_follows_the_language()
    {
        var previous = Localizer.Instance.Language;
        try
        {
            var item = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 10)
            {
                Status = JobStatus.Encoding
            };

            Localizer.Instance.Language = AppLanguage.English;
            Assert.Equal("Encoding", item.StatusLabel);

            Localizer.Instance.Language = AppLanguage.German;
            Assert.Equal("Kodiert", item.StatusLabel);
        }
        finally
        {
            Localizer.Instance.Language = previous;
        }
    }

    [Fact]
    public void Accessible_summary_conveys_state_without_colour()
    {
        var item = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 10);
        item.ApplyOutcome(
            CompressionOutcome.Failed("a.jpg", 10, CompressionErrorCategory.EngineFailed, "x"));

        Assert.Contains("a.jpg", item.AccessibleSummary);
        Assert.Contains(item.StatusLabel, item.AccessibleSummary);
        Assert.Contains("EngineFailed", item.AccessibleSummary);
    }
}
