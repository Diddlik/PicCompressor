using PicCompressor.Application;
using PicCompressor.Gui.Services;

namespace PicCompressor.Desktop;

public sealed class ApplicationEngineCatalogService(IEngineCatalog engineCatalog)
    : IEngineCatalogService
{
    public async Task<IReadOnlyList<EngineAvailability>> GetEnginesAsync(
        CancellationToken cancellationToken)
    {
        var capabilities = await engineCatalog
            .DetectCapabilitiesAsync(cancellationToken)
            .ConfigureAwait(false);
        return capabilities
            .Select(capability => new EngineAvailability(
                capability.EngineId,
                capability.IsAvailable,
                capability.BuildVersion,
                capability.UnavailableReason))
            .ToArray();
    }
}
