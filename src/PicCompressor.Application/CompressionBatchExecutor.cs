using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed record CompressionJobProgress(Guid JobId, JobStatus Status);

public sealed class CompressionBatchExecutor(
    ICompressionJobExecutor jobExecutor,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<CompressionExecutionResult>> ExecuteAsync(
        IReadOnlyList<CompressionJob> jobs,
        int maxParallelism,
        CancellationToken cancellationToken) =>
        ExecuteAsync(jobs, maxParallelism, null, cancellationToken);

    public Task<IReadOnlyList<CompressionExecutionResult>> ExecuteAsync(
        IReadOnlyList<CompressionJob> jobs,
        int maxParallelism,
        IProgress<CompressionJobProgress>? progress,
        CancellationToken cancellationToken) =>
        ExecuteAsync(jobs, CompressionResourceLimits.CpuOnly(maxParallelism), progress, cancellationToken);

    public Task<IReadOnlyList<CompressionExecutionResult>> ExecuteAsync(
        IReadOnlyList<CompressionJob> jobs,
        CompressionResourceLimits limits,
        CancellationToken cancellationToken) =>
        ExecuteAsync(jobs, limits, null, cancellationToken);

    public async Task<IReadOnlyList<CompressionExecutionResult>> ExecuteAsync(
        IReadOnlyList<CompressionJob> jobs,
        CompressionResourceLimits limits,
        IProgress<CompressionJobProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(limits);

        var gate = new CompressionResourceGate(limits);
        var tasks = jobs.Select(ExecuteJobAsync).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);

        async Task<CompressionExecutionResult> ExecuteJobAsync(CompressionJob job)
        {
            var guetzli = string.Equals(
                job.EngineSettings.EngineId,
                GuetzliSettings.GuetzliEngineId,
                StringComparison.Ordinal);
            var memory = guetzli ? job.InputImageInfo.PixelCount * limits.GuetzliBytesPerPixel : 0;

            progress?.Report(new(job.Id, JobStatus.WaitingForResources));
            try
            {
                await gate.AcquireAsync(guetzli, memory, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var canceled = Canceled(job);
                progress?.Report(new(job.Id, canceled.Status));
                return canceled;
            }

            try
            {
                progress?.Report(new(job.Id, JobStatus.Encoding));
                CompressionExecutionResult result;
                try
                {
                    result = await jobExecutor
                        .ExecuteAsync(job, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    result = Canceled(job);
                }

                if (result.Status is JobStatus.Succeeded)
                {
                    progress?.Report(new(job.Id, JobStatus.Finalizing));
                }

                progress?.Report(new(job.Id, result.Status));
                return result;
            }
            finally
            {
                gate.Release(guetzli, memory);
            }
        }
    }

    private CompressionExecutionResult Canceled(CompressionJob job)
    {
        var now = clock.GetUtcNow();
        return new(
            job.Id,
            JobStatus.Canceled,
            job.InputPath,
            job.OutputPath,
            job.EngineSettings.EngineId,
            null,
            job.InputImageInfo.FileSizeBytes,
            null,
            now,
            now,
            false,
            false,
            null,
            CompressionErrorCategory.Canceled,
            "Job was canceled before encoding started.");
    }
}
