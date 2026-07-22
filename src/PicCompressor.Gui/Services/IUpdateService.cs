namespace PicCompressor.Gui.Services;

/// <summary>Ergebnis einer Updateprüfung (MP-006).</summary>
/// <param name="UpdateAvailable">Ob eine neuere Version verfügbar ist.</param>
/// <param name="Version">Version des gefundenen Updates; <c>null</c>, wenn keines vorliegt.</param>
public sealed record UpdateCheck(bool UpdateAvailable, string? Version);

/// <summary>
/// Zugang zum Update-Lifecycle (MP-006, Abschnitt 15). Die GUI kennt nur diesen Port, nicht
/// VeloPack. Signatur- und Paketprüfung liegen im Adapter und dürfen nicht umgangen werden;
/// die CLI verwendet diesen Port nicht und installiert nie selbstständig Updates.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Meldet, ob Updates in dieser Ausführung verwaltet werden können. Ein nicht installierter
    /// Entwicklungslauf hat keinen Updatekontext und meldet <c>false</c>.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Prüft die Releasequelle auf eine neuere Version.</summary>
    Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lädt das zuletzt in <see cref="CheckAsync"/> gefundene Update herunter, wendet es an und
    /// startet die Anwendung neu. Fortschritt wird über <paramref name="progress"/> in Prozent
    /// gemeldet. Ohne zuvor gefundenes Update passiert nichts.
    /// </summary>
    Task DownloadAndApplyAsync(IProgress<int>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Standard, solange kein Update-Adapter verdrahtet ist (Entwicklungslauf, Tests). Es gibt
/// keinen Updatekontext, deshalb meldet die Oberfläche das offen, statt Aktualität zu behaupten.
/// </summary>
public sealed class UnconfiguredUpdateService : IUpdateService
{
    public bool IsSupported => false;

    public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new UpdateCheck(false, null));

    public Task DownloadAndApplyAsync(IProgress<int>? progress, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
