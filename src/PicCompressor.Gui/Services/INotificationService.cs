namespace PicCompressor.Gui.Services;

/// <summary>
/// Abschluss- und Fehlerbenachrichtigungen (MP-003). Reiner Host-Adapter (Abschnitt 14.1): die
/// GUI übergibt bereits lokalisierte Texte, die plattformabhängige Anzeige liegt im Host. Die
/// Benachrichtigung ist capability-gesteuert und nie Voraussetzung für die Verarbeitung — ein
/// nicht unterstützter Host zeigt einfach nichts an, ohne Fehler.
/// </summary>
public interface INotificationService
{
    Task ShowAsync(string title, string body, bool isError);
}

/// <summary>
/// Standard ohne verdrahteten Host: es wird nichts angezeigt. So bleiben ViewModel-Tests und
/// nicht verdrahtete Läufe funktionsfähig, ohne eine Wirkung vorzutäuschen.
/// </summary>
public sealed class UnconfiguredNotificationService : INotificationService
{
    public Task ShowAsync(string title, string body, bool isError) => Task.CompletedTask;
}
