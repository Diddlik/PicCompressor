using PicCompressor.Web;

namespace PicCompressor.Web.Tests;

public sealed class ScanControlTests
{
    [Fact]
    public async Task Configuration_publish_is_atomic_with_scan_start()
    {
        var control = new ScanControl();
        var loadedVersion = control.ConfigurationVersion;
        using var saveEntered = new ManualResetEventSlim();
        using var allowSaveToFinish = new ManualResetEventSlim();

        var publish = Task.Run(
            () => control.PublishConfiguration(
                () =>
                {
                    saveEntered.Set();
                    allowSaveToFinish.Wait(TestContext.Current.CancellationToken);
                    return 42;
                }),
            TestContext.Current.CancellationToken);
        Assert.True(
            saveEntered.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        var beginScan = Task.Run(
            () => control.TryBeginScan(loadedVersion),
            TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(beginScan.IsCompleted);

        allowSaveToFinish.Set();

        Assert.Equal(42, await publish);
        Assert.False(await beginScan);
    }

    [Fact]
    public void Configuration_version_prevents_starting_a_stale_scan()
    {
        var control = new ScanControl();
        var loadedVersion = control.ConfigurationVersion;

        control.ConfigurationChanged();

        Assert.False(control.TryBeginScan(loadedVersion));
        Assert.True(control.TryBeginScan(control.ConfigurationVersion));
    }

    [Fact]
    public async Task Configuration_signal_is_not_lost_when_watch_is_already_pending()
    {
        var control = new ScanControl();
        control.Signal("watch");
        control.ConfigurationChanged();

        var first = await control.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("configuration", first);

        var second = await control.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("watch", second);
    }
}
