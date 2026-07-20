namespace PicCompressor.Domain;

public enum JobStatus
{
    Queued,
    Validating,
    WaitingForResources,
    Encoding,
    Finalizing,
    Succeeded,
    Failed,
    Canceled
}

public static class JobStatusTransitions
{
    public static bool CanTransitionTo(this JobStatus current, JobStatus next) =>
        current switch
        {
            JobStatus.Queued => next is JobStatus.Validating or JobStatus.Canceled,
            JobStatus.Validating => next is JobStatus.WaitingForResources
                or JobStatus.Failed
                or JobStatus.Canceled,
            JobStatus.WaitingForResources => next is JobStatus.Encoding or JobStatus.Canceled,
            JobStatus.Encoding => next is JobStatus.Finalizing
                or JobStatus.Failed
                or JobStatus.Canceled,
            JobStatus.Finalizing => next is JobStatus.Succeeded
                or JobStatus.Failed
                or JobStatus.Canceled,
            _ => false
        };
}
