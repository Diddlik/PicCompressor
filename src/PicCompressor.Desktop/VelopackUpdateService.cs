using PicCompressor.Gui.Services;
using Velopack;
using Velopack.Sources;

namespace PicCompressor.Desktop;

/// <summary>
/// VeloPack-Adapter für die Updateoberfläche (MP-006). Bindet den GUI-Port an den
/// GitHub-Releasekanal; VeloPack prüft Paket und Signatur vor der Installation. Außerhalb einer
/// installierten Anwendung gibt es keinen Updatekontext, dann meldet der Adapter das offen.
/// </summary>
internal sealed class VelopackUpdateService : IUpdateService
{
    private readonly UpdateManager manager;
    private UpdateInfo? pending;

    public VelopackUpdateService(string repoUrl, bool includePrereleases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoUrl);
        manager = new UpdateManager(new GithubSource(repoUrl, null, includePrereleases));
    }

    public bool IsSupported => manager.IsInstalled;

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken)
    {
        if (!manager.IsInstalled)
        {
            return new UpdateCheck(false, null);
        }

        pending = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return pending is null
            ? new UpdateCheck(false, null)
            : new UpdateCheck(true, pending.TargetFullRelease.Version.ToString());
    }

    public async Task DownloadAndApplyAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (pending is null)
        {
            return;
        }

        await manager
            .DownloadUpdatesAsync(pending, p => progress?.Report(p), cancellationToken)
            .ConfigureAwait(false);
        // Beendet den Prozess und installiert; die Signatur-/Paketprüfung von VeloPack läuft
        // dabei und wird nicht umgangen.
        manager.ApplyUpdatesAndRestart(pending.TargetFullRelease, null);
    }
}
