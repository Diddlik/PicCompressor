namespace PicCompressor.Gui.Services;

/// <summary>
/// Host-Aktionen für eine einzelne Datei (MP-002): im Standardprogramm öffnen, im Dateimanager
/// zeigen, Pfad in die Zwischenablage kopieren. Reiner Host-Adapter (Abschnitt 14.1, MP-003):
/// die GUI kennt nur diesen Port, der Desktop Host verdrahtet die plattformabhängige Umsetzung.
/// Jede Methode behandelt ihre eigenen Fehler und wirft nicht; eine fehlgeschlagene Nebenaktion
/// darf die Oberfläche nicht stören.
/// </summary>
public interface IFileActionService
{
    Task OpenFileAsync(string path);

    Task RevealInFolderAsync(string path);

    Task CopyPathAsync(string path);
}

/// <summary>
/// Standard ohne verdrahteten Host: die Aktionen sind wirkungslos. So bleiben ViewModel-Tests und
/// nicht verdrahtete Läufe funktionsfähig, ohne eine Wirkung vorzutäuschen.
/// </summary>
public sealed class UnconfiguredFileActionService : IFileActionService
{
    public Task OpenFileAsync(string path) => Task.CompletedTask;

    public Task RevealInFolderAsync(string path) => Task.CompletedTask;

    public Task CopyPathAsync(string path) => Task.CompletedTask;
}
