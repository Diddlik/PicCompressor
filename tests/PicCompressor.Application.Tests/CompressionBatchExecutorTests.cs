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

    [Fact]
    public async Task ExecuteAsync_caps_guetzli_parallelism_by_the_memory_budget()
    {
        var jobExecutor = new TrackingExecutor();
        var jobs = Enumerable.Range(0, 4).Select(index => CreateGuetzliJob(index, 100, 100)).ToArray();
        // Each job weighs 10000 px * 1 byte; the 25000-byte budget admits two at a time.
        var limits = new CompressionResourceLimits(
            maxParallelism: 4, maxGuetzliParallelism: 4, guetzliMemoryBudgetBytes: 25000, guetzliBytesPerPixel: 1);

        var results = await new CompressionBatchExecutor(jobExecutor)
            .ExecuteAsync(jobs, limits, CancellationToken.None);

        Assert.Equal(2, jobExecutor.MaxConcurrent);
        Assert.All(results, result => Assert.Equal(JobStatus.Succeeded, result.Status));
    }

    [Fact]
    public async Task ExecuteAsync_caps_guetzli_parallelism_by_the_dedicated_limit()
    {
        var jobExecutor = new TrackingExecutor();
        var jobs = Enumerable.Range(0, 3).Select(index => CreateGuetzliJob(index, 10, 10)).ToArray();
        var limits = new CompressionResourceLimits(
            maxParallelism: 4, maxGuetzliParallelism: 1, guetzliMemoryBudgetBytes: long.MaxValue, guetzliBytesPerPixel: 0);

        await new CompressionBatchExecutor(jobExecutor).ExecuteAsync(jobs, limits, CancellationToken.None);

        Assert.Equal(1, jobExecutor.MaxConcurrent);
    }

    [Fact]
    public async Task ExecuteAsync_runs_an_oversized_guetzli_job_alone_instead_of_starving_it()
    {
        var jobExecutor = new TrackingExecutor();
        // Each job needs 10000 bytes but the budget is only 5000, so none fits alongside another.
        var jobs = Enumerable.Range(0, 3).Select(index => CreateGuetzliJob(index, 100, 100)).ToArray();
        var limits = new CompressionResourceLimits(
            maxParallelism: 2, maxGuetzliParallelism: 2, guetzliMemoryBudgetBytes: 5000, guetzliBytesPerPixel: 1);

        var results = await new CompressionBatchExecutor(jobExecutor)
            .ExecuteAsync(jobs, limits, CancellationToken.None);

        Assert.Equal(1, jobExecutor.MaxConcurrent);
        Assert.All(results, result => Assert.Equal(JobStatus.Succeeded, result.Status));
    }

    [Fact]
    public async Task ExecuteAsync_does_not_gate_jpegli_on_the_guetzli_budget()
    {
        var jobExecutor = new TrackingExecutor();
        var jobs = Enumerable.Range(0, 3).Select(CreateJob).ToArray();
        // A tiny budget and single Guetzli slot must not touch Jpegli jobs.
        var limits = new CompressionResourceLimits(
            maxParallelism: 3, maxGuetzliParallelism: 1, guetzliMemoryBudgetBytes: 1, guetzliBytesPerPixel: 1_000_000);

        await new CompressionBatchExecutor(jobExecutor).ExecuteAsync(jobs, limits, CancellationToken.None);

        Assert.Equal(3, jobExecutor.MaxConcurrent);
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

    private static CompressionJob CreateGuetzliJob(int index, int width, int height) =>
        new(
            Guid.NewGuid(),
            Path.GetFullPath($"guetzli-input-{index}.png"),
            Path.GetFullPath($"guetzli-output-{index}.jpg"),
            new GuetzliSettings(90),
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Discard,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, width, height, 100));

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
