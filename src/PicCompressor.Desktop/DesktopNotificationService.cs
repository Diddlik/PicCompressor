using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using PicCompressor.Gui.Services;

namespace PicCompressor.Desktop;

/// <summary>
/// Host-Adapter für Abschluss- und Fehlerbenachrichtigungen (MP-003). Zeigt eine In-App-Meldung
/// über den <see cref="WindowNotificationManager"/> des Hauptfensters. Solange kein Fenster
/// vorliegt (etwa vor dem Start), ist die Anzeige nicht verfügbar und geschieht nichts — die
/// Benachrichtigung ist nie Voraussetzung für die Verarbeitung (Abschnitt 14.1).
/// </summary>
public sealed class DesktopNotificationService : INotificationService
{
    private WindowNotificationManager? manager;
    private Window? host;

    public Task ShowAsync(string title, string body, bool isError)
    {
        var window = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
        {
            return Task.CompletedTask;
        }

        // Den Manager je Fenster einmal aufbauen; ein neuer je Meldung häufte Adorner an.
        if (!ReferenceEquals(host, window))
        {
            manager = new WindowNotificationManager(window) { MaxItems = 3 };
            host = window;
        }

        manager!.Show(
            new Notification(
                title,
                body,
                isError ? NotificationType.Error : NotificationType.Success));
        return Task.CompletedTask;
    }
}
