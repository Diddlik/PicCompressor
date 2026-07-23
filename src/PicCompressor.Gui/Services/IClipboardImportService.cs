namespace PicCompressor.Gui.Services;

/// <summary>
/// Einreihen aus der Zwischenablage (MP-003). Reiner Host-Adapter (Abschnitt 14.1): die GUI kennt
/// nur diesen Port und bekommt Pfade zurück, die sie wie jede andere Eingabe prüft und einreiht.
/// Bilddaten ohne Datei legt der Adapter zuvor als verwaltete temporäre Eingabe ab; eine leere
/// oder unpassende Zwischenablage liefert eine leere Liste und ist kein Fehler.
/// </summary>
public interface IClipboardImportService
{
    Task<IReadOnlyList<string>> ReadImportPathsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Standard ohne verdrahteten Host: die Zwischenablage bleibt unerreichbar. So bleiben
/// ViewModel-Tests und nicht verdrahtete Läufe funktionsfähig, ohne eine Wirkung vorzutäuschen.
/// </summary>
public sealed class UnconfiguredClipboardImportService : IClipboardImportService
{
    public Task<IReadOnlyList<string>> ReadImportPathsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
