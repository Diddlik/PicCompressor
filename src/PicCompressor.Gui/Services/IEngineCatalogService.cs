using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.Services;

/// <summary>
/// Verfügbarkeit einer Engine. Eine nicht verfügbare Engine MUSS eine konkrete Ursache tragen
/// (docs/requirements.md Abschnitt 4.2).
/// </summary>
public sealed record EngineAvailability(
    string EngineId,
    bool IsAvailable,
    string? Version,
    string? UnavailableReason)
{
    public static EngineAvailability Unavailable(string engineId, string reason) =>
        new(engineId, false, null, reason);
}

public interface IEngineCatalogService
{
    Task<IReadOnlyList<EngineAvailability>> GetEnginesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Standardimplementierung ohne verdrahteten Adapter: beide Engines gelten als nicht verfügbar.
/// Das verhindert den Start der Anwendung nicht (Abschnitt 4.2).
/// </summary>
public sealed class UnconfiguredEngineCatalogService : IEngineCatalogService
{
    public Task<IReadOnlyList<EngineAvailability>> GetEnginesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reason = Localizer.Instance["Error_NoEngineCatalog"];

        IReadOnlyList<EngineAvailability> engines =
        [
            EngineAvailability.Unavailable(EngineIds.Jpegli, reason),
            EngineAvailability.Unavailable(EngineIds.Guetzli, reason)
        ];

        return Task.FromResult(engines);
    }
}
