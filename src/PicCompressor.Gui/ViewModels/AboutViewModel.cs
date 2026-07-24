using System.Diagnostics;
using System.Reflection;
using PicCompressor.Gui.Localization;

namespace PicCompressor.Gui.ViewModels;

/// <summary>Ein Dritt-Bestandteil des Pakets mit seiner Lizenz (Issue #2).</summary>
public sealed record CreditEntry(string Name, string Role, string License);

/// <summary>
/// „Über PicCompressor“: Version und Credits der gebündelten nativen und verwalteten
/// Bibliotheken. Namen, Lizenzbezeichner und Versionen bleiben unübersetzt und plattformidentisch
/// (Abschnitt 4.3); nur beschreibende Texte kommen aus den Ressourcen. Die Lizenzhinweise selbst
/// liegen als Dateien im Paket (Abschnitt 15); diese Liste macht sie in der Oberfläche sichtbar.
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    public AboutViewModel(Action? requestClose = null)
    {
        Version = ResolveInformationalVersion();
        // Eigennamen/Bezeichner bleiben unübersetzt und plattformidentisch (Abschnitt 4.3).
        OpenRepositoryCommand = new RelayCommand(OpenRepository);
        CloseCommand = new RelayCommand(() => requestClose?.Invoke());
    }

    /// <summary>Produktversion aus der Assembly; im Entwicklungslauf ein Platzhalter.</summary>
    public string Version { get; }

    public string VersionLabel => Localizer.Instance.Format("About_Version", Version);

    /// <summary>Eigenname — unübersetzt (Abschnitt 4.3).</summary>
    public string Author => "Diddlik";

    /// <summary>Stabiler Lizenzbezeichner — unübersetzt.</summary>
    public string License => "MIT";

    public string RepositoryUrl => "https://github.com/Diddlik/PicCompressor";

    public RelayCommand OpenRepositoryCommand { get; }

    public RelayCommand CloseCommand { get; }

    private void OpenRepository()
    {
        // Standard-Browser über die Shell-Zuordnung öffnen, ohne Interpreter-Aufruf.
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Ein fehlender Browser darf den Dialog nicht abstürzen lassen.
        }
    }

    /// <summary>
    /// Gebündelte Komponenten. Native Encoder und ihre Abhängigkeiten stehen zuerst, dann die
    /// verwalteten Pakete. Die Lizenzdateien liegen im Paketordner <c>licenses</c>.
    /// </summary>
    public IReadOnlyList<CreditEntry> Components { get; } =
    [
        new("Jpegli", "JPEG encoder", "BSD-3-Clause"),
        new("Guetzli", "Legacy JPEG encoder", "Apache-2.0"),
        new("Highway", "SIMD runtime", "Apache-2.0"),
        new("skcms", "Color management", "BSD-3-Clause"),
        new("libpng", "PNG decoding", "libpng"),
        new("zlib", "Compression", "Zlib"),
        new("Avalonia", "UI framework", "MIT"),
        new("Velopack", "Installer and updates", "MIT"),
        new("System.CommandLine", "CLI parsing", "MIT"),
        new("Microsoft.Data.Sqlite", "History storage", "MIT"),
    ];

    private static string ResolveInformationalVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0-dev";
        }

        // Die Buildmetadaten hinter '+' (Git-Hash) gehören nicht in die Anzeige.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informational[..plus] : informational;
    }
}
