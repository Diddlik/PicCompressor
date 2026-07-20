using PicCompressor.Domain;
using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.Services;

/// <summary>
/// Einziger Weg der Oberfläche zur Kompression. Die konkrete Verdrahtung auf die
/// Application-Anwendungsfälle liegt außerhalb dieses Projekts; die GUI kennt nur diesen Port.
/// </summary>
public interface ICompressionService
{
    Task<CompressionOutcome> CompressAsync(
        CompressionRequest request,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<CompressionOutcome>> CompressBatchAsync(
        IReadOnlyList<CompressionRequest> requests,
        int maxParallelism,
        IProgress<CompressionBatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        var outcomes = new List<CompressionOutcome>(requests.Count);
        for (var index = 0; index < requests.Count; index++)
        {
            var current = index;
            var jobProgress = progress is null
                ? null
                : new Progress<CompressionProgress>(
                    value => progress.Report(new(current, value)));
            outcomes.Add(
                await CompressAsync(requests[index], jobProgress, cancellationToken)
                    .ConfigureAwait(false));
        }

        return outcomes;
    }
}

/// <summary>
/// Standardimplementierung, solange kein Adapter verdrahtet ist. Sie meldet jeden Auftrag als
/// <see cref="CompressionErrorCategory.EngineUnavailable"/> mit konkreter Ursache und
/// simuliert bewusst keinen Erfolg.
/// </summary>
public sealed class UnconfiguredCompressionService : ICompressionService
{
    public Task<CompressionOutcome> CompressAsync(
        CompressionRequest request,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            CompressionOutcome.Failed(
                request.InputPath,
                0,
                CompressionErrorCategory.EngineUnavailable,
                Localizer.Instance["Error_NoCompressionService"]).Validate());
    }
}
