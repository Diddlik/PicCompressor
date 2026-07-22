using PicCompressor.Gui.Localization;
using PicCompressor.Gui.Services;

namespace PicCompressor.Gui.ViewModels;

/// <summary>
/// Steuert die Desktop-Updateoberfläche (MP-006): manuelle Prüfung, Downloadfortschritt und
/// verständliche Fehlerzustände. Die eigentliche Signatur- und Paketprüfung liegt im
/// <see cref="IUpdateService"/>-Adapter und wird hier nicht umgangen.
/// </summary>
public sealed class UpdateViewModel : ObservableObject
{
    private enum Phase
    {
        Idle,
        Checking,
        UpToDate,
        Available,
        Downloading,
        Failed
    }

    private readonly IUpdateService updateService;
    private Phase phase = Phase.Idle;
    private string? availableVersion;
    private string? errorDetail;
    private int downloadProgress;

    public UpdateViewModel(IUpdateService? updateService = null)
    {
        this.updateService = updateService ?? new UnconfiguredUpdateService();
        CheckCommand = new AsyncRelayCommand(() => CheckAsync(CancellationToken.None), () => IsSupported);
        InstallCommand = new AsyncRelayCommand(
            () => InstallAsync(CancellationToken.None), () => phase is Phase.Available);
    }

    public AsyncRelayCommand CheckCommand { get; }

    public AsyncRelayCommand InstallCommand { get; }

    /// <summary>Ob diese Ausführung Updates verwalten kann; sonst bleibt die Prüfung verborgen.</summary>
    public bool IsSupported => updateService.IsSupported;

    public bool CanInstall => phase is Phase.Available;

    public bool IsDownloading => phase is Phase.Downloading;

    /// <summary>Downloadfortschritt in Prozent (Abschnitt 11); nur während des Downloads sinnvoll.</summary>
    public int DownloadProgress
    {
        get => downloadProgress;
        private set => SetProperty(ref downloadProgress, value);
    }

    /// <summary>Verständlicher Zustandstext; die Version selbst ist ein stabiler Bezeichner.</summary>
    public string StatusText => phase switch
    {
        Phase.Checking => Localizer.Instance["Update_StatusChecking"],
        Phase.UpToDate => Localizer.Instance["Update_StatusUpToDate"],
        Phase.Available => Localizer.Instance.Format("Update_StatusAvailable", availableVersion ?? string.Empty),
        Phase.Downloading => Localizer.Instance["Update_StatusDownloading"],
        Phase.Failed => Localizer.Instance.Format("Update_StatusError", errorDetail ?? string.Empty),
        _ => Localizer.Instance["Update_StatusIdle"]
    };

    /// <summary>Prüft manuell auf ein Update und spiegelt das Ergebnis im Zustandstext.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        SetPhase(Phase.Checking);
        try
        {
            var result = await updateService.CheckAsync(cancellationToken).ConfigureAwait(true);
            availableVersion = result.Version;
            SetPhase(result.UpdateAvailable ? Phase.Available : Phase.UpToDate);
        }
        catch (Exception exception)
        {
            errorDetail = exception.Message;
            SetPhase(Phase.Failed);
        }
    }

    /// <summary>Lädt das gefundene Update, wendet es an und startet neu.</summary>
    public async Task InstallAsync(CancellationToken cancellationToken)
    {
        DownloadProgress = 0;
        SetPhase(Phase.Downloading);
        var progress = new Progress<int>(value => DownloadProgress = value);
        try
        {
            // Bei Erfolg startet der Adapter die Anwendung neu; kehrt der Aufruf zurück,
            // gilt das Update als angewendet und die Oberfläche fällt in den Ruhezustand.
            await updateService.DownloadAndApplyAsync(progress, cancellationToken).ConfigureAwait(true);
            SetPhase(Phase.Idle);
        }
        catch (Exception exception)
        {
            errorDetail = exception.Message;
            SetPhase(Phase.Failed);
        }
    }

    private void SetPhase(Phase next)
    {
        phase = next;
        Raise(nameof(StatusText));
        Raise(nameof(CanInstall));
        Raise(nameof(IsDownloading));
        CheckCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
    }
}
