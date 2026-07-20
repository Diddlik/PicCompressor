namespace PicCompressor.Application;

public interface IEngineCatalog
{
    Task<IReadOnlyList<EngineCapability>> DetectCapabilitiesAsync(
        CancellationToken cancellationToken);
}

public sealed class EngineCatalog : IEngineCatalog
{
    private readonly ICompressionEngine[] engines;

    public EngineCatalog(IEnumerable<ICompressionEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        this.engines = engines.ToArray();

        var duplicate = this.engines
            .GroupBy(engine => engine.EngineId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Duplicate engine ID: {duplicate.Key}",
                nameof(engines));
        }
    }

    public async Task<IReadOnlyList<EngineCapability>> DetectCapabilitiesAsync(
        CancellationToken cancellationToken)
    {
        var capabilities = new List<EngineCapability>(engines.Length);
        foreach (var engine in engines)
        {
            try
            {
                capabilities.Add(
                    await engine.DetectCapabilityAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                capabilities.Add(
                    EngineCapability.Unavailable(engine.EngineId, exception.Message));
            }
        }

        return capabilities;
    }
}
