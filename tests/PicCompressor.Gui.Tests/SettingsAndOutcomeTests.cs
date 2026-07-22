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

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5000, 3650)]
    public void History_retention_days_is_clamped(int requested, int expected)
    {
        var settings = new SettingsViewModel { HistoryRetentionDays = requested };

        Assert.Equal(expected, settings.HistoryRetentionDays);
    }

    [Fact]
    public void History_retention_survives_a_restart()
    {
        var store = new PicCompressor.Application.InMemoryApplicationSettingsStore();
        _ = new SettingsViewModel(store) { HistoryRetentionDays = 30 };

        Assert.Equal(30, new SettingsViewModel(store).HistoryRetentionDays);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5000, 1024)]
    public void Log_max_file_megabytes_is_clamped(int requested, int expected)
    {
        var settings = new SettingsViewModel { LogMaxFileMegabytes = requested };

        Assert.Equal(expected, settings.LogMaxFileMegabytes);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 100)]
    public void Log_retained_files_is_clamped(int requested, int expected)
    {
        var settings = new SettingsViewModel { LogRetainedFiles = requested };

        Assert.Equal(expected, settings.LogRetainedFiles);
    }

    [Fact]
    public void Log_rotation_limits_survive_a_restart()
    {
        var store = new PicCompressor.Application.InMemoryApplicationSettingsStore();
        _ = new SettingsViewModel(store) { LogMaxFileMegabytes = 20, LogRetainedFiles = 3 };

        var restarted = new SettingsViewModel(store);
        Assert.Equal(20, restarted.LogMaxFileMegabytes);
        Assert.Equal(3, restarted.LogRetainedFiles);
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
    public void Guetzli_settings_carry_the_clamped_quality()
    {
        var settings = new SettingsViewModel { IsGuetzli = true };

        var built = Assert.IsType<GuetzliSettings>(settings.TryBuildEngineSettings());
        Assert.True(built.Quality >= GuetzliSettings.MinimumQuality);
        Assert.Equal(GuetzliSettings.GuetzliEngineId, built.EngineId);
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

    /// <summary>
    /// Einstellungen überleben einen Neustart (Abschnitt 13.2). Ein zweites
    /// ViewModel über demselben Speicher steht für den nächsten Programmstart.
    /// </summary>
    [Fact]
    public void Settings_survive_a_restart()
    {
        var store = new PicCompressor.Application.InMemoryApplicationSettingsStore();
        var previousLanguage = Localizer.Instance.Language;
        try
        {
            var first = new SettingsViewModel(store)
            {
                Quality = 61,
                ChromaSubsampling = JpegliChromaSubsampling.Subsampling444,
                ExifPolicy = ExifPolicy.Private,
                ColorProfilePolicy = ColorProfilePolicy.Srgb,
                CollisionPolicy = CollisionPolicy.Rename,
                Suffix = "_klein",
                ParallelJobs = 2
            };
            first.Appearance.Theme = AppTheme.Dark;
            first.Appearance.Language = AppLanguage.German;

            var restarted = new SettingsViewModel(store);

            Assert.Equal(61, restarted.Quality);
            Assert.Equal(JpegliChromaSubsampling.Subsampling444, restarted.ChromaSubsampling);
            Assert.Equal(ExifPolicy.Private, restarted.ExifPolicy);
            Assert.Equal(ColorProfilePolicy.Srgb, restarted.ColorProfilePolicy);
            Assert.Equal(CollisionPolicy.Rename, restarted.CollisionPolicy);
            Assert.Equal("_klein", restarted.Suffix);
            Assert.Equal(2, restarted.ParallelJobs);
            Assert.Equal(AppTheme.Dark, restarted.Appearance.Theme);
            Assert.Equal(AppLanguage.German, restarted.Appearance.Language);
        }
        finally
        {
            Localizer.Instance.Language = previousLanguage;
        }
    }

    /// <summary>
    /// Felder ohne Entsprechung in der Oberfläche dürfen beim Speichern nicht
    /// verloren gehen.
    /// </summary>
    [Fact]
    public void Saving_preserves_fields_the_user_interface_does_not_expose()
    {
        var store = new PicCompressor.Application.InMemoryApplicationSettingsStore();
        store.Save(new PicCompressor.Application.ApplicationSettings
        {
            HistoryRetentionDays = 7
        });

        var settings = new SettingsViewModel(store) { Quality = 55 };

        Assert.Equal(7, store.Load().HistoryRetentionDays);
        Assert.Equal(55, store.Load().Quality);
    }

    /// <summary>
    /// Fortschrittsberichte werden über <see cref="IProgress{T}"/> asynchron zugestellt
    /// und können dem Endergebnis nachlaufen. Ein terminaler Zustand darf dadurch nicht
    /// wieder verlassen werden (Abschnitt 6.2).
    /// </summary>
    [Fact]
    public void A_late_progress_report_does_not_revive_a_terminal_job()
    {
        var item = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 10);
        item.ApplyOutcome(
            new CompressionOutcome(
                JobStatus.Succeeded, "a.jpg", "a.out.jpg", 10, 5, true, null, null, null));

        item.ApplyProgress(new CompressionProgress(JobStatus.Encoding, 0.5));

        Assert.Equal(JobStatus.Succeeded, item.Status);
        Assert.True(item.IsTerminal);
    }

    [Fact]
    public void Resetting_for_a_new_run_is_the_only_way_out_of_a_terminal_state()
    {
        var item = new QueueItemViewModel("a.jpg", EngineIds.Jpegli, 10);
        item.ApplyOutcome(
            CompressionOutcome.Failed("a.jpg", 10, CompressionErrorCategory.EngineFailed, "x"));

        item.Status = JobStatus.Encoding;
        Assert.Equal(JobStatus.Failed, item.Status);

        item.ResetForRun();
        Assert.Equal(JobStatus.Queued, item.Status);
        Assert.False(item.IsTerminal);
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
