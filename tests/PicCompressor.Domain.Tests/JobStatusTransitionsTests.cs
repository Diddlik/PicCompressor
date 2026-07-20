using PicCompressor.Domain;

namespace PicCompressor.Domain.Tests;

public sealed class JobStatusTransitionsTests
{
    [Theory]
    [InlineData(JobStatus.Queued, JobStatus.Validating)]
    [InlineData(JobStatus.Validating, JobStatus.WaitingForResources)]
    [InlineData(JobStatus.WaitingForResources, JobStatus.Encoding)]
    [InlineData(JobStatus.Encoding, JobStatus.Finalizing)]
    [InlineData(JobStatus.Finalizing, JobStatus.Succeeded)]
    [InlineData(JobStatus.Encoding, JobStatus.Canceled)]
    public void Allows_defined_transitions(JobStatus current, JobStatus next)
    {
        Assert.True(current.CanTransitionTo(next));
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Canceled)]
    public void Terminal_states_cannot_be_left(JobStatus current)
    {
        foreach (var next in Enum.GetValues<JobStatus>())
        {
            Assert.False(current.CanTransitionTo(next));
        }
    }
}
