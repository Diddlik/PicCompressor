using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Tests;

public sealed class UpdateViewModelTests
{
    private sealed class FakeUpdateService : IUpdateService
    {
        public bool IsSupported { get; init; } = true;
        public UpdateCheck Result { get; init; } = new(false, null);
        public Exception? CheckError { get; init; }
        public Exception? ApplyError { get; init; }
        public int ApplyCalls { get; private set; }
        public int ReportedProgress { get; private set; }

        public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken) =>
            CheckError is not null
                ? Task.FromException<UpdateCheck>(CheckError)
                : Task.FromResult(Result);

        public Task DownloadAndApplyAsync(IProgress<int>? progress, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            progress?.Report(100);
            ReportedProgress = 100;
            return ApplyError is not null ? Task.FromException(ApplyError) : Task.CompletedTask;
        }
    }

    [Fact]
    public void Unsupported_run_hides_the_check()
    {
        var update = new UpdateViewModel(new FakeUpdateService { IsSupported = false });

        Assert.False(update.IsSupported);
        Assert.False(update.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_available_update_enables_install_and_names_the_version()
    {
        var update = new UpdateViewModel(new FakeUpdateService { Result = new(true, "0.3.0") });

        await update.CheckAsync(CancellationToken.None);

        Assert.True(update.CanInstall);
        Assert.True(update.InstallCommand.CanExecute(null));
        Assert.Contains("0.3.0", update.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_update_reports_up_to_date_and_keeps_install_disabled()
    {
        var update = new UpdateViewModel(new FakeUpdateService { Result = new(false, null) });

        await update.CheckAsync(CancellationToken.None);

        Assert.False(update.CanInstall);
        Assert.Equal(Localizer.Instance["Update_StatusUpToDate"], update.StatusText);
    }

    [Fact]
    public async Task A_failed_check_surfaces_the_reason()
    {
        var update = new UpdateViewModel(
            new FakeUpdateService { CheckError = new InvalidOperationException("network down") });

        await update.CheckAsync(CancellationToken.None);

        Assert.False(update.CanInstall);
        Assert.Contains("network down", update.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_downloads_and_reports_progress()
    {
        var service = new FakeUpdateService { Result = new(true, "0.3.0") };
        var update = new UpdateViewModel(service);
        await update.CheckAsync(CancellationToken.None);

        await update.InstallAsync(CancellationToken.None);

        Assert.Equal(1, service.ApplyCalls);
        Assert.Equal(100, update.DownloadProgress);
    }

    [Fact]
    public async Task A_failed_install_surfaces_the_reason()
    {
        var service = new FakeUpdateService
        {
            Result = new(true, "0.3.0"),
            ApplyError = new InvalidOperationException("disk full")
        };
        var update = new UpdateViewModel(service);
        await update.CheckAsync(CancellationToken.None);

        await update.InstallAsync(CancellationToken.None);

        Assert.Contains("disk full", update.StatusText, StringComparison.Ordinal);
    }
}
