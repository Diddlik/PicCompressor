using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using PicCompressor.Gui.Services;

namespace PicCompressor.Desktop;

/// <summary>
/// Host-Adapter für die Pro-Datei-Aktionen (MP-002). Öffnen und Anzeigen laufen über den
/// Standard-Dateihandler des Betriebssystems ohne Shell-Interpreter; das Kopieren nutzt die
/// Zwischenablage des aktiven Fensters. Jede Aktion schluckt ihre eigenen Fehler, damit eine
/// fehlgeschlagene Nebenaktion die Oberfläche nicht stört (Abschnitt 14.3).
/// </summary>
public sealed class DesktopFileActionService : IFileActionService
{
    public Task OpenFileAsync(string path)
    {
        Try(() => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
        return Task.CompletedTask;
    }

    public Task RevealInFolderAsync(string path)
    {
        Try(() =>
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // /select markiert die Datei im Explorer; die Anführungszeichen halten Leerzeichen zusammen.
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", ["-R", path]);
            }
            else
            {
                // Kein portables „markieren“ unter Linux: den enthaltenden Ordner öffnen.
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                }
            }
        });
        return Task.CompletedTask;
    }

    public async Task CopyPathAsync(string path)
    {
        var clipboard = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(path).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Ein fehlgeschlagenes Kopieren ist kein Produktfehler.
        }
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            // Der Start des Dateihandlers darf die Anwendung nicht beenden.
        }
    }
}
