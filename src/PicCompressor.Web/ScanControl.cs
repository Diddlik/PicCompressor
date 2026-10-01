using System.Threading.Channels;

namespace PicCompressor.Web;

public sealed record ScanStatus(
    string State,
    string? CurrentFolder,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? NextRunAt,
    string? Trigger,
    int Succeeded,
    int Failed,
    int Unchanged,
    int Unstable,
    string? LastError)
{
    public static ScanStatus Idle { get; } = new(
        "idle", null, null, null, null, null, 0, 0, 0, 0, null);
}

public interface IScanControl
{
    ScanStatus Status { get; }

    bool Trigger();

    void ConfigurationChanged();
}

public sealed class ScanControl : IScanControl
{
    private readonly Channel<byte> signals = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly Lock configurationGate = new();
    private ScanStatus status = ScanStatus.Idle;
    private long configurationVersion;
    private int configurationPending;
    private int manualPending;
    private int watchPending;

    public ScanStatus Status => Volatile.Read(ref status);

    internal long ConfigurationVersion
    {
        get
        {
            lock (configurationGate)
            {
                return configurationVersion;
            }
        }
    }

    public bool Trigger()
    {
        var queued = Interlocked.Exchange(ref manualPending, 1) == 0;
        Wake();
        return queued;
    }

    public void ConfigurationChanged()
    {
        PublishConfiguration(static () => true);
    }

    internal T PublishConfiguration<T>(Func<T> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        T result;
        lock (configurationGate)
        {
            result = publish();
            configurationVersion++;
            Interlocked.Exchange(ref configurationPending, 1);
        }

        Wake();
        return result;
    }

    internal void Signal(string source)
    {
        if (!string.Equals(source, "watch", StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        Interlocked.Exchange(ref watchPending, 1);
        Wake();
    }

    internal async ValueTask<string> WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryTakePending(out var source))
            {
                return source;
            }

            await signals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal bool TryTakePending(out string source)
    {
        if (Interlocked.Exchange(ref configurationPending, 0) == 1)
        {
            source = "configuration";
            return true;
        }

        if (Interlocked.Exchange(ref manualPending, 0) == 1)
        {
            source = "manual";
            return true;
        }

        if (Interlocked.Exchange(ref watchPending, 0) == 1)
        {
            source = "watch";
            return true;
        }

        source = string.Empty;
        return false;
    }

    internal bool TryBeginScan(long loadedConfigurationVersion)
    {
        lock (configurationGate)
        {
            if (configurationVersion != loadedConfigurationVersion)
            {
                return false;
            }

            Interlocked.Exchange(ref configurationPending, 0);
            return true;
        }
    }

    internal void Update(ScanStatus value) => Volatile.Write(ref status, value);

    private void Wake() => signals.Writer.TryWrite(0);
}
