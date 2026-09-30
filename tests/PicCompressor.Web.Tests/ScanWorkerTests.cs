using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Infrastructure;
using PicCompressor.Web;

namespace PicCompressor.Web.Tests;

public sealed class ScanWorkerTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"piccompressor-worker-{Guid.NewGuid():N}");

    [Fact]
    public async Task Worker_uses_the_validated_configured_lock_path()
    {
        var service = CreateService();
        var configuredLock = Path.Combine(StateRoot, "custom.lock");
        service.Save(
            ValidConfiguration() with { LockPath = configuredLock },
            service.CreateDefault().Revision);
        using var heldLock = RunLock.TryAcquire(configuredLock);
        Assert.NotNull(heldLock);
        var control = new ScanControl();
        var worker = CreateWorker(service, control);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(
                () => control.Status.State == "blocked",
                TimeSpan.FromSeconds(2));

            Assert.Contains("scan lock", control.Status.LastError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Worker_keeps_the_configured_lock_while_idle()
    {
        var service = CreateService();
        var configuredLock = Path.Combine(StateRoot, "idle.lock");
        service.Save(
            ValidConfiguration(intervalSeconds: 30) with { LockPath = configuredLock },
            service.CreateDefault().Revision);
        var control = new ScanControl();
        var worker = CreateWorker(service, control);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(
                () => control.Status.State == "idle" && control.Status.FinishedAt is not null,
                TimeSpan.FromSeconds(2));

            using var competingLock = RunLock.TryAcquire(configuredLock);
            Assert.Null(competingLock);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Worker_retries_the_configured_lock_after_contention()
    {
        var service = CreateService();
        var configuredLock = Path.Combine(StateRoot, "retry.lock");
        service.Save(
            ValidConfiguration(intervalSeconds: 30) with { LockPath = configuredLock },
            service.CreateDefault().Revision);
        var heldLock = RunLock.TryAcquire(configuredLock);
        Assert.NotNull(heldLock);
        var control = new ScanControl();
        var worker = CreateWorker(service, control);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => control.Status.State == "blocked", TimeSpan.FromSeconds(2));
            heldLock.Dispose();
            heldLock = null;
            Assert.True(control.Trigger());

            await WaitUntilAsync(
                () => control.Status.Trigger == "manual" && control.Status.FinishedAt is not null,
                TimeSpan.FromSeconds(2));
        }
        finally
        {
            heldLock?.Dispose();
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Top_level_configuration_errors_remain_visible_as_error_state()
    {
        var service = CreateService();
        new JsonScanConfigurationStore(ConfigPath).Save(
            ValidConfiguration() with { Folders = [] });
        var control = new ScanControl();
        var worker = CreateWorker(service, control);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(
                () => control.Status.FinishedAt is not null,
                TimeSpan.FromSeconds(2));

            Assert.Equal("error", control.Status.State);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Configuration_changed_during_watch_debounce_reloads_before_scanning()
    {
        var service = CreateService();
        var firstLock = Path.Combine(StateRoot, "first.lock");
        var secondLock = Path.Combine(StateRoot, "second.lock");
        var saved = service.Save(
            ValidConfiguration(intervalSeconds: 30) with
            {
                Mode = ScanMode.Watch,
                DebounceSeconds = 1,
                LockPath = firstLock
            },
            service.CreateDefault().Revision);
        var control = new ScanControl();
        var worker = CreateWorker(service, control);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(
                () => control.Status.State == "idle" && control.Status.FinishedAt is not null,
                TimeSpan.FromSeconds(2));
            File.WriteAllText(Path.Combine(InputRoot, "changed.jpg"), "watch");
            await Task.Delay(150, TestContext.Current.CancellationToken);
            service.Save(
                ValidConfiguration(intervalSeconds: 30) with
                {
                    Mode = ScanMode.Watch,
                    DebounceSeconds = 1,
                    LockPath = secondLock
                },
                saved.Revision);
            control.ConfigurationChanged();

            await WaitUntilAsync(
                () => LockIsFree(firstLock) && !LockIsFree(secondLock)
                    && control.Status.Trigger == "configuration",
                TimeSpan.FromSeconds(3));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Symlink_introduced_during_watch_debounce_is_rejected_before_scanning()
    {
        var service = CreateService();
        service.Save(
            ValidConfiguration(intervalSeconds: 30) with
            {
                Mode = ScanMode.Watch,
                DebounceSeconds = 1
            },
            service.CreateDefault().Revision);
        var control = new ScanControl();
        var worker = CreateWorker(service, control);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(
                () => control.Status.State == "idle" && control.Status.FinishedAt is not null,
                TimeSpan.FromSeconds(2));
            File.WriteAllText(Path.Combine(InputRoot, "changed.jpg"), "watch");
            await Task.Delay(150, TestContext.Current.CancellationToken);

            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            Directory.Delete(InputRoot, recursive: true);
            Directory.CreateSymbolicLink(InputRoot, outside);

            await WaitUntilAsync(
                () => control.Status.State == "error"
                    && control.Status.LastError?.Contains(
                        "symbolic link",
                        StringComparison.OrdinalIgnoreCase) == true,
                TimeSpan.FromSeconds(3));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Interval_completion_does_not_leave_a_waiter_that_consumes_the_next_trigger()
    {
        var service = CreateService();
        service.Save(ValidConfiguration(intervalSeconds: 1), service.CreateDefault().Revision);
        var control = new ScanControl();
        var worker = CreateWorker(service, control);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(
                () => control.Status.Trigger == "interval",
                TimeSpan.FromSeconds(3));

            Assert.True(control.Trigger());

            await WaitUntilAsync(
                () => control.Status.Trigger == "manual",
                TimeSpan.FromMilliseconds(600));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    private WebConfigurationService CreateService()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(StateRoot);
        Directory.CreateDirectory(ConfigRoot);
        Directory.CreateDirectory(InputRoot);
        Directory.CreateDirectory(OutputRoot);
        return new WebConfigurationService(ConfigPath, DataRoot, StateRoot);
    }

    private ScanWorker CreateWorker(WebConfigurationService service, ScanControl control)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PicCompressor:StateRoot"] = StateRoot
                })
            .Build();
        return new ScanWorker(service, control, configuration, NullLogger<ScanWorker>.Instance);
    }

    private ScanConfiguration ValidConfiguration(int intervalSeconds = 300) => new(
        SchemaVersion: 1,
        Mode: ScanMode.Interval,
        IntervalSeconds: intervalSeconds,
        DebounceSeconds: 1,
        StableForSeconds: 0,
        Parallelism: 1,
        TimeoutSeconds: 300,
        StateDirectory: StateRoot,
        LockPath: Path.Combine(StateRoot, "configured.lock"),
        LogPath: Path.Combine(StateRoot, "piccompressor.jsonl"),
        History: false,
        Defaults: new(
            Quality: 80,
            ChromaSubsampling: JpegliChromaSubsampling.Subsampling420,
            ProgressiveLevel: 2,
            Exif: ExifPolicy.Remove,
            ColorProfile: ColorProfilePolicy.Preserve,
            AlphaBackground: "#FFFFFF",
            Collision: CollisionPolicy.Skip,
            LargerOutput: LargerOutputPolicy.Discard,
            MinSavingsPercent: 5,
            Suffix: "_compressed"),
        Folders:
        [
            new(
                Name: "photos",
                Input: InputRoot,
                Output: OutputRoot,
                Recursive: true)
        ]);

    private static bool LockIsFree(string path)
    {
        using var runLock = RunLock.TryAcquire(path);
        return runLock is not null;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!predicate())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private string DataRoot => Path.Combine(root, "data");
    private string StateRoot => Path.Combine(root, "state");
    private string ConfigRoot => Path.Combine(root, "config");
    private string ConfigPath => Path.Combine(ConfigRoot, "piccompressor.json");
    private string InputRoot => Path.Combine(DataRoot, "photos");
    private string OutputRoot => Path.Combine(DataRoot, "output");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
