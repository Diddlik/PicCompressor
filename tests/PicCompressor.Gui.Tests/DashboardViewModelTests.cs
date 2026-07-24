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
    public async Task AddPaths_accepts_only_supported_extensions()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var jpg = TempFiles.CreateImage(directory, "a.jpg");
        var png = TempFiles.CreateImage(directory, "b.PNG");
        var txt = TempFiles.CreateImage(directory, "c.txt");

        var added = await dashboard.AddPathsAsync([jpg, png, txt]);

        Assert.Equal(2, added);
        Assert.Equal(2, dashboard.Queue.Count);
        Assert.DoesNotContain(dashboard.Queue, item => item.InputPath == txt);
    }

    [Fact]
    public async Task AddPaths_ignores_duplicates()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var jpg = TempFiles.CreateImage(directory, "a.jpg");

        await dashboard.AddPathsAsync([jpg]);
        var second = await dashboard.AddPathsAsync([jpg]);

        Assert.Equal(0, second);
        Assert.Single(dashboard.Queue);
    }

    [Fact]
    public async Task AddPaths_reports_when_nothing_was_supported()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var txt = TempFiles.CreateImage(directory, "c.txt");

        await dashboard.AddPathsAsync([txt]);

        Assert.True(dashboard.HasDropHint);
    }

    [Fact]
    public async Task Unconfigured_service_never_reports_success()
    {
        var dashboard = Create(new UnconfiguredCompressionService());
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

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
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

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
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

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
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

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
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        var item = Assert.Single(dashboard.Queue);
        Assert.Equal(JobStatus.Canceled, item.Status);
        Assert.Equal(CompressionErrorCategory.Canceled, item.ErrorCategory);
        Assert.False(item.OutputPublished);
    }

    [Fact]
    public async Task Guetzli_jobs_are_submitted_with_guetzli_settings()
    {
        var service = FakeCompressionService.Succeeding();
        var dashboard = Create(service);
        dashboard.Settings.IsGuetzli = true;
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        // Guetzli ist eine echte Engine mit Domain-Modell; die Oberfläche reicht den Job ein.
        // Ob die Engine ausführbar ist, entscheidet die Engine-Capability im Executor
        // (Abschnitt 4.2), nicht das ViewModel.
        var request = Assert.Single(service.Requests);
        Assert.IsType<GuetzliSettings>(request.EngineSettings);
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
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

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
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        Assert.Equal(directory, Assert.Single(service.Requests).OutputDirectory);
    }

    [Fact]
    public async Task Commands_reflect_queue_state()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());

        Assert.False(dashboard.CompressAllCommand.CanExecute(null));
        Assert.False(dashboard.ClearCompletedCommand.CanExecute(null));
        Assert.False(dashboard.CancelCommand.CanExecute(null));

        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

        Assert.True(dashboard.CompressAllCommand.CanExecute(null));
        Assert.True(dashboard.RemoveAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task ClearCompleted_removes_only_terminal_jobs()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);
        await RunAsync(dashboard);
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "b.jpg")]);

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

        await dashboard.AddPathsAsync(
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
        await dashboard.AddPathsAsync(
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
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "retry.jpg")]);
        await RunAsync(dashboard);

        dashboard.RetryFailedCommand.Execute(null);
        await RunAsync(dashboard);

        Assert.Equal(firstJobId, service.Requests[1].PredecessorJobId);
        Assert.Equal(JobStatus.Succeeded, Assert.Single(dashboard.Queue).Status);
    }

    [Fact]
    public async Task Discovery_can_be_cancelled_and_leaves_the_queue_empty()
    {
        var discovery = new FakeInputDiscovery { BlockUntilCancelled = true };
        var dashboard = new DashboardViewModel(
            new SettingsViewModel(), FakeCompressionService.Succeeding(), discovery);
        var jpg = TempFiles.CreateImage(directory, "a.jpg");

        var adding = dashboard.AddPathsAsync([jpg]);
        // Der Aufruf blockiert in der Discovery, bis der Abbruch greift.
        while (!dashboard.IsDiscovering)
        {
            await Task.Yield();
        }

        dashboard.CancelDiscoveryCommand.Execute(null);
        var added = await adding;

        Assert.Equal(0, added);
        Assert.False(dashboard.IsDiscovering);
        Assert.Empty(dashboard.Queue);
        Assert.True(dashboard.HasDropHint);
    }

    [Fact]
    public void RemoveItem_removes_only_that_item()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var a = new QueueItemViewModel(Path.Combine(directory, "a.jpg"), "jpegli", 10);
        var b = new QueueItemViewModel(Path.Combine(directory, "b.jpg"), "jpegli", 10);
        dashboard.Queue.Add(a);
        dashboard.Queue.Add(b);

        dashboard.RemoveItemCommand.Execute(a);

        var remaining = Assert.Single(dashboard.Queue);
        Assert.Same(b, remaining);
    }

    [Fact]
    public async Task RetryItem_requeues_a_failed_job_with_a_predecessor_reference()
    {
        var dashboard = Create(
            FakeCompressionService.Failing(CompressionErrorCategory.EngineFailed));
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);
        await RunAsync(dashboard);
        var item = Assert.Single(dashboard.Queue);
        Assert.True(dashboard.RetryItemCommand.CanExecute(item));

        dashboard.RetryItemCommand.Execute(item);

        Assert.Equal(JobStatus.Queued, item.Status);
        Assert.False(item.IsTerminal);
    }

    [Fact]
    public void CompareItem_raises_compare_requested_with_the_item()
    {
        var dashboard = Create(FakeCompressionService.Succeeding());
        var item = new QueueItemViewModel(Path.Combine(directory, "a.jpg"), "jpegli", 10);
        QueueItemViewModel? requested = null;
        dashboard.CompareRequested += (_, value) => requested = value;

        dashboard.CompareItemCommand.Execute(item);

        Assert.Same(item, requested);
    }

    [Fact]
    public void CopyPath_and_reveal_use_the_input_path_until_an_output_is_published()
    {
        var actions = new FakeFileActionService();
        var dashboard = new DashboardViewModel(
            new SettingsViewModel(), FakeCompressionService.Succeeding(),
            new FakeInputDiscovery(), actions);
        var input = Path.Combine(directory, "a.jpg");
        var item = new QueueItemViewModel(input, "jpegli", 10);

        dashboard.CopyPathItemCommand.Execute(item);
        dashboard.RevealItemCommand.Execute(item);

        Assert.Equal(input, actions.CopiedPath);
        Assert.Equal(input, actions.RevealedPath);
        // Ohne veröffentlichte Ausgabe ist "Öffnen" nicht ausführbar.
        Assert.False(dashboard.OpenItemCommand.CanExecute(item));
    }

    [Fact]
    public async Task Open_targets_the_published_output_after_a_successful_run()
    {
        var actions = new FakeFileActionService();
        var dashboard = new DashboardViewModel(
            new SettingsViewModel(), FakeCompressionService.Succeeding(outputSizeBytes: 40),
            new FakeInputDiscovery(), actions);
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);
        await RunAsync(dashboard);
        var item = Assert.Single(dashboard.Queue);

        Assert.True(dashboard.OpenItemCommand.CanExecute(item));
        dashboard.OpenItemCommand.Execute(item);

        Assert.Equal(item.OutputPath, actions.OpenedPath);
    }

    [Fact]
    public async Task Paste_enqueues_what_the_clipboard_adapter_reports()
    {
        var image = TempFiles.CreateImage(directory, "pasted.png");
        var dashboard = new DashboardViewModel(
            new SettingsViewModel(), FakeCompressionService.Succeeding(),
            new FakeInputDiscovery(), new FakeFileActionService(),
            clipboardImport: new FakeClipboardImportService(image));

        await dashboard.PasteAsync();

        Assert.Equal(image, Assert.Single(dashboard.Queue).InputPath);
        Assert.Null(dashboard.DropHint);
    }

    [Fact]
    public async Task Paste_reports_an_empty_clipboard_instead_of_enqueueing()
    {
        var dashboard = new DashboardViewModel(
            new SettingsViewModel(), FakeCompressionService.Succeeding(),
            new FakeInputDiscovery(), new FakeFileActionService(),
            clipboardImport: new FakeClipboardImportService());

        await dashboard.PasteAsync();

        Assert.Empty(dashboard.Queue);
        Assert.True(dashboard.HasDropHint);
    }

    [Fact]
    public async Task A_finished_batch_notifies_with_the_result_counts()
    {
        var notifications = new FakeNotificationService();
        var dashboard = new DashboardViewModel(
            new SettingsViewModel(), FakeCompressionService.Succeeding(),
            new FakeInputDiscovery(), new FakeFileActionService(),
            notifications: notifications);
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        Assert.Equal(1, notifications.Count);
        Assert.False(notifications.WasError);
        Assert.Contains("1", notifications.Body);
    }

    [Fact]
    public async Task A_failed_job_marks_the_notification_as_an_error()
    {
        var notifications = new FakeNotificationService();
        var dashboard = new DashboardViewModel(
            new SettingsViewModel(),
            FakeCompressionService.Failing(CompressionErrorCategory.EngineFailed),
            new FakeInputDiscovery(), new FakeFileActionService(),
            notifications: notifications);
        await dashboard.AddPathsAsync([TempFiles.CreateImage(directory, "a.jpg")]);

        await RunAsync(dashboard);

        Assert.Equal(1, notifications.Count);
        Assert.True(notifications.WasError);
    }

    private DashboardViewModel Create(ICompressionService service) =>
        new(new SettingsViewModel(), service, new FakeInputDiscovery(), new FakeFileActionService());

    private static async Task RunAsync(DashboardViewModel dashboard)
    {
        // Direkt die Aufgabe abwarten statt den async-void-Befehl abzupollen: ein
        // Zeitbudget macht den Test unter paralleler Last flaky.
        await dashboard.CompressAllAsync();

        Assert.All(dashboard.Queue, item => Assert.True(item.IsTerminal));
    }
}
