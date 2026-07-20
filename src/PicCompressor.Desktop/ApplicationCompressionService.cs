using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;

namespace PicCompressor.Desktop;

public sealed class ApplicationCompressionService(
    CompressionJobFactory jobFactory,
    ICompressionJobExecutor jobExecutor)
    : ICompressionService
{
    public async Task<CompressionOutcome> CompressAsync(
        CompressionRequest request,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        progress?.Report(new(JobStatus.Validating));

        CompressionJob job;
        try
        {
            job = jobFactory.Create(
                new(
                    request.InputPath,
                    request.EngineSettings,
                    request.ExifPolicy,
                    request.ColorProfilePolicy,
                    request.AlphaBackground,
                    request.CollisionPolicy,
                    request.LargerOutputPolicy,
                    request.OutputDirectory,
                    request.Suffix,
                    PredecessorJobId: request.PredecessorJobId));
        }
        catch (JobCreationException exception)
        {
            return CompressionOutcome.Failed(
                request.InputPath,
                0,
                exception.Category,
                exception.Message);
        }

        progress?.Report(new(JobStatus.WaitingForResources));
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(JobStatus.Encoding));
        var result = await jobExecutor
            .ExecuteAsync(job, cancellationToken)
            .ConfigureAwait(false);
        if (result.Status is JobStatus.Succeeded)
        {
            progress?.Report(new(JobStatus.Finalizing));
        }

        return new CompressionOutcome(
            result.Status,
            result.InputPath,
            result.OutputPath,
            result.InputSizeBytes,
            result.EncodedSizeBytes,
            result.OutputPublished,
            result.Warning,
            result.ErrorCategory,
            result.ErrorText,
            result.JobId).Validate();
    }

    public async Task<IReadOnlyList<CompressionOutcome>> CompressBatchAsync(
        IReadOnlyList<CompressionRequest> requests,
        int maxParallelism,
        IProgress<CompressionBatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);

        var outcomes = new CompressionOutcome?[requests.Count];
        var jobs = new List<CompressionJob>(requests.Count);
        var jobIndexes = new Dictionary<Guid, int>(requests.Count);
        var reservedOutputPaths = new List<string>(requests.Count);
        for (var index = 0; index < requests.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = requests[index];
            progress?.Report(new(index, new(JobStatus.Validating)));
            try
            {
                var job = jobFactory.Create(
                    new(
                        request.InputPath,
                        request.EngineSettings,
                        request.ExifPolicy,
                        request.ColorProfilePolicy,
                        request.AlphaBackground,
                        request.CollisionPolicy,
                        request.LargerOutputPolicy,
                        request.OutputDirectory,
                        request.Suffix,
                        PredecessorJobId: request.PredecessorJobId),
                    reservedOutputPaths);
                jobs.Add(job);
                jobIndexes.Add(job.Id, index);
                reservedOutputPaths.Add(job.OutputPath);
            }
            catch (JobCreationException exception)
            {
                outcomes[index] = CompressionOutcome.Failed(
                    request.InputPath,
                    0,
                    exception.Category,
                    exception.Message);
            }
        }

        var batchProgress = progress is null
            ? null
            : new ForwardingProgress<CompressionJobProgress>(
                update =>
                {
                    if (update.Status is not (
                        JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled))
                    {
                        progress.Report(
                            new(jobIndexes[update.JobId], new(update.Status)));
                    }
                });
        var results = await new CompressionBatchExecutor(jobExecutor)
            .ExecuteAsync(jobs, maxParallelism, batchProgress, cancellationToken)
            .ConfigureAwait(false);
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            outcomes[jobIndexes[result.JobId]] = Map(result);
        }

        return outcomes.Select(outcome => outcome!).ToArray();
    }

    private static CompressionOutcome Map(CompressionExecutionResult result) =>
        new CompressionOutcome(
            result.Status,
            result.InputPath,
            result.OutputPath,
            result.InputSizeBytes,
            result.EncodedSizeBytes,
            result.OutputPublished,
            result.Warning,
            result.ErrorCategory,
            result.ErrorText,
            result.JobId).Validate();

    private sealed class ForwardingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
