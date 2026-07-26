using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

public sealed class CompressionBatchExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_limits_parallelism_and_preserves_input_order()
    {
        var jobExecutor = new TrackingExecutor();
        var jobs = Enumerable.Range(0, 4).Select(CreateJob).ToArray();
        var batchExecutor = new CompressionBatchExecutor(jobExecutor);

        var results = await batchExecutor.ExecuteAsync(jobs, 2, CancellationToken.None);

        Assert.Equal(2, jobExecutor.MaxConcurrent);
        Assert.Equal(jobs.Select(job => job.Id), results.Select(result => result.JobId));
    }

    [Fact]
    public async Task ExecuteAsync_returns_canceled_results_for_jobs_waiting_for_resources()
    {
        var jobs = Enumerable.Range(0, 2).Select(CreateJob).ToArray();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var batchExecutor = new CompressionBatchExecutor(new TrackingExecutor());

        var results = await batchExecutor.ExecuteAsync(jobs, 1, cancellationSource.Token);

        Assert.All(results, result => Assert.Equal(JobStatus.Canceled, result.Status));
    }

    [Fact]
    public async Task ExecuteAsync_reports_ordered_resource_and_terminal_phases()
    {
        var job = CreateJob(0);
        var updates = new List<CompressionJobProgress>();
        var progress = new InlineProgress<CompressionJobProgress>(updates.Add);
        var batchExecutor = new CompressionBatchExecutor(new TrackingExecutor());

        await batchExecutor.ExecuteAsync([job], 1, progress, CancellationToken.None);

        Assert.Equal(
            [JobStatus.WaitingForResources, JobStatus.Encoding, JobStatus.Finalizing, JobStatus.Succeeded],
            updates.Select(update => update.Status));
        Assert.All(updates, update => Assert.Equal(job.Id, update.JobId));
    }

    private static CompressionJob CreateJob(int index) =>
        new(
            Guid.NewGuid(),
            Path.GetFullPath($"input-{index}.png"),
            Path.GetFullPath($"output-{index}.jpg"),
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Discard,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, 1, 1, 100));

    private sealed class TrackingExecutor : ICompressionJobExecutor
    {
        private int concurrent;

        public int MaxConcurrent { get; private set; }

        public async Task<CompressionExecutionResult> ExecuteAsync(
            CompressionJob job,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref concurrent);
            MaxConcurrent = Math.Max(MaxConcurrent, current);
            await Task.Delay(25, cancellationToken);
            Interlocked.Decrement(ref concurrent);
            return new(
                job.Id,
                JobStatus.Succeeded,
                job.InputPath,
                job.OutputPath,
                job.EngineSettings.EngineId,
                "test",
                job.InputImageInfo.FileSizeBytes,
                50,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                true,
                true,
                null,
                null,
                null);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
