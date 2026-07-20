using PicCompressor.Domain;
using PicCompressor.Gui.Services;
using PicCompressor.Gui.ViewModels;

namespace PicCompressor.Gui.Tests;

public sealed class DashboardViewModelTests : IDisposable
{
    private readonly string directory = TempFiles.CreateDirectory();

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumfehler dürfen den Test nicht kippen.
        }
    }

    [Fact]
    public void AddPaths_accepts_only_supported_extensions()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var jpg = TempFiles.CreateImage(directory, "a.jpg");
        var png = TempFiles.CreateImage(directory, "b.PNG");
        var txt = TempFiles.CreateImage(directory, "c.txt");

        var added = dashboard.AddPaths([jpg, png, txt]);

        Assert.Equal(2, added);
        Assert.Equal(2, dashboard.Queue.Count);
        Assert.DoesNotContain(dashboard.Queue, item => item.InputPath == txt);
    }

    [Fact]
    public void AddPaths_ignores_duplicates()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var jpg = TempFiles.CreateImage(directory, "a.jpg");

        dashboard.AddPaths([jpg]);
        var second = dashboard.AddPaths([jpg]);

        Assert.Equal(0, second);
        Assert.Single(dashboard.Queue);
    }

    [Fact]
    public void AddPaths_reports_when_nothing_was_supported()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var txt = TempFiles.CreateImage(directory, "c.txt");

        dashboard.AddPaths([txt]);

        Assert.True(dashboard.HasDropHint);
    }

    [Fact]
    public async Task Unconfigured_service_never_reports_success()
    {
        var dashboard = Create(new UnconfiguredCompressionService());
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        var item = Assert.Single(dashboard.Queue);
        Assert.Equal(JobStatus.Failed, item.Status);
        Assert.Equal(CompressionErrorCategory.EngineUnavailable, item.ErrorCategory);
        Assert.False(item.OutputPublished);
        Assert.False(item.CanCompare);
    }

    [Fact]
    public async Task Successful_outcome_is_taken_over_verbatim()
    {
        var dashboard = Create(FakeCompressionService.Succeeding(outputSizeBytes: 250));
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        var item = Assert.Single(dashboard.Queue);
        Assert.Equal(JobStatus.Succeeded, item.Status);
        Assert.Null(item.ErrorCategory);
        Assert.True(item.OutputPublished);
        Assert.True(item.CanCompare);
        Assert.Equal(250, item.OutputSizeBytes);
    }

    [Fact]
    public async Task Engine_failure_is_surfaced_with_its_category()
    {
        var dashboard = Create(
            FakeCompressionService.Failing(CompressionErrorCategory.OutputValidationFailed));
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        var item = Assert.Single(dashboard.Queue);
        Assert.Equal(JobStatus.Failed, item.Status);
        Assert.Equal(CompressionErrorCategory.OutputValidationFailed, item.ErrorCategory);
        Assert.Contains("OutputValidationFailed", item.ErrorSummary);
    }

    [Fact]
    public async Task Unexpected_exception_becomes_an_unexpected_error_not_a_success()
    {
        var dashboard = Create(FakeCompressionService.Throwing());
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        var item = Assert.Single(dashboard.Queue);
        Assert.Equal(JobStatus.Failed, item.Status);
        Assert.Equal(CompressionErrorCategory.Unexpected, item.ErrorCategory);
        Assert.False(item.OutputPublished);
    }

    [Fact]
    public async Task Cancellation_yields_a_canceled_job_without_output()
    {
        var dashboard = Create(FakeCompressionService.Canceling());
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        var item = Assert.Single(dashboard.Queue);
        Assert.Equal(JobStatus.Canceled, item.Status);
        Assert.Equal(CompressionErrorCategory.Canceled, item.ErrorCategory);
        Assert.False(item.OutputPublished);
    }

    [Fact]
    public async Task Guetzli_is_reported_unavailable_instead_of_being_run()
    {
        var service = FakeCompressionService.Succeeding();
        var dashboard = Create(service);
        dashboard.Settings.IsGuetzli = true;
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        var item = Assert.Single(dashboard.Queue);
        Assert.Equal(CompressionErrorCategory.EngineUnavailable, item.ErrorCategory);
        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task Request_carries_the_configured_policies()
    {
        var service = FakeCompressionService.Succeeding();
        var dashboard = Create(service);
        dashboard.Settings.ExifPolicy = ExifPolicy.Private;
        dashboard.Settings.ColorProfilePolicy = ColorProfilePolicy.Srgb;
        dashboard.Settings.CollisionPolicy = CollisionPolicy.Rename;
        dashboard.Settings.LargerOutputPolicy = LargerOutputPolicy.Keep;
        dashboard.Settings.Suffix = "_small";
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        var request = Assert.Single(service.Requests);
        Assert.Equal(ExifPolicy.Private, request.ExifPolicy);
        Assert.Equal(ColorProfilePolicy.Srgb, request.ColorProfilePolicy);
        Assert.Equal(CollisionPolicy.Rename, request.CollisionPolicy);
        Assert.Equal(LargerOutputPolicy.Keep, request.LargerOutputPolicy);
        Assert.Equal("_small", request.Suffix);
        Assert.Null(request.OutputDirectory);
    }

    [Fact]
    public async Task Custom_output_directory_is_only_sent_when_selected()
    {
        var service = FakeCompressionService.Succeeding();
        var dashboard = Create(service);
        dashboard.Settings.OutputDirectory = directory;
        dashboard.Settings.UsesCustomDirectory = true;
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        Assert.Equal(directory, Assert.Single(service.Requests).OutputDirectory);
    }

    [Fact]
    public void Commands_reflect_queue_state()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());

        Assert.False(dashboard.CompressAllCommand.CanExecute(null));
        Assert.False(dashboard.ClearCompletedCommand.CanExecute(null));
        Assert.False(dashboard.CancelCommand.CanExecute(null));

        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);

        Assert.True(dashboard.CompressAllCommand.CanExecute(null));
        Assert.True(dashboard.RemoveAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearCompleted_removes_only_terminal_jobs()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        dashboard.AddPaths([TempFiles.CreateImage(directory, "a.jpg")]);
        await RunAsync(dashboard);
        dashboard.AddPaths([TempFiles.CreateImage(directory, "b.jpg")]);

        dashboard.ClearCompletedCommand.Execute(null);

        var remaining = Assert.Single(dashboard.Queue);
        Assert.EndsWith("b.jpg", remaining.InputPath);
    }

    [Fact]
    public async Task JobCompleted_is_raised_once_per_job()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var completed = new List<QueueItemViewModel>();
        dashboard.JobCompleted += (_, item) => completed.Add(item);

        dashboard.AddPaths(
        [
            TempFiles.CreateImage(directory, "a.jpg"),
            TempFiles.CreateImage(directory, "b.jpg")
        ]);
        await RunAsync(dashboard);

        Assert.Equal(2, completed.Count);
    }

    [Fact]
    public async Task CompressAll_delegates_parallelism_to_the_queue_service()
    {
        var service = FakeCompressionService.Succeeding();
        var dashboard = Create(service);
        dashboard.Settings.ParallelJobs = 2;
        dashboard.AddPaths(
        [
            TempFiles.CreateImage(directory, "parallel-a.jpg"),
            TempFiles.CreateImage(directory, "parallel-b.jpg")
        ]);

        await RunAsync(dashboard);

        Assert.Equal(2, service.LastBatchParallelism);
    }

    [Fact]
    public async Task Retry_failed_creates_a_new_job_with_predecessor_reference()
    {
        var firstJobId = Guid.NewGuid();
        var attempts = 0;
        var service = new FakeCompressionService((request, _, _) =>
        {
            attempts++;
            return Task.FromResult(
                attempts == 1
                    ? CompressionOutcome.Failed(
                        request.InputPath,
                        100,
                        CompressionErrorCategory.EngineFailed,
                        "failed",
                        firstJobId)
                    : new CompressionOutcome(
                        JobStatus.Succeeded,
                        request.InputPath,
                        request.InputPath + ".jpg",
                        100,
                        50,
                        true,
                        null,
                        null,
                        null,
                        Guid.NewGuid()));
        });
        var dashboard = Create(service);
        dashboard.AddPaths([TempFiles.CreateImage(directory, "retry.jpg")]);
        await RunAsync(dashboard);

        dashboard.RetryFailedCommand.Execute(null);
        await RunAsync(dashboard);

        Assert.Equal(firstJobId, service.Requests[1].PredecessorJobId);
        Assert.Equal(JobStatus.Succeeded, Assert.Single(dashboard.Queue).Status);
    }

    private DashboardViewModel Create(ICompressionService service) =>
        new(new SettingsViewModel(), service);

    private static async Task RunAsync(DashboardViewModel dashboard)
    {
        dashboard.CompressAllCommand.Execute(null);

        // Der Befehl ist asynchron; warten, bis kein Job mehr offen ist.
        for (var attempt = 0; attempt < 200 && dashboard.Queue.Any(item => !item.IsTerminal); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.All(dashboard.Queue, item => Assert.True(item.IsTerminal));
    }
}
