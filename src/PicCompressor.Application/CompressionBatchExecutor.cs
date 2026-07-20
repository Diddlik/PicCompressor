using PicCompressor.Domain;

namespace PicCompressor.Application;

public sealed record CompressionJobProgress(Guid JobId, JobStatus Status);

public sealed class CompressionBatchExecutor(
    ICompressionJobExecutor jobExecutor,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<CompressionExecutionResult>> ExecuteAsync(
        IReadOnlyList<CompressionJob> jobs,
        int maxParallelism,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(jobs, maxParallelism, null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<CompressionExecutionResult>> ExecuteAsync(
        IReadOnlyList<CompressionJob> jobs,
        int maxParallelism,
        IProgress<CompressionJobProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);

        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
        var tasks = jobs.Select(ExecuteJobAsync).ToArray();
        return await Task.WhenAll(tasks).ConfigureAwait(false);

        async Task<CompressionExecutionResult> ExecuteJobAsync(CompressionJob job)
        {
            progress?.Report(new(job.Id, JobStatus.WaitingForResources));
            try
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                semaphore.Release();
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
