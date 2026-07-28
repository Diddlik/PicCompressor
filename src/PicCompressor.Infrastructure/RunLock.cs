namespace PicCompressor.Infrastructure;

/// <summary>
/// Exklusiver Lauf-Lock für wiederkehrende Scans (MP-005). Das Betriebssystem gibt die Sperre
/// beim Prozessende frei, auch nach einem Absturz; es bleibt also kein Zustand zurück, der einen
/// späteren Lauf dauerhaft blockiert.
/// </summary>
public sealed class RunLock : IDisposable
{
    private readonly FileStream stream;

    private RunLock(FileStream stream) => this.stream = stream;

    /// <summary>
    /// Erwirbt die Sperre oder liefert <c>null</c>, wenn bereits ein Lauf sie hält.
    /// </summary>
    public static RunLock? TryAcquire(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            return new RunLock(
                new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => stream.Dispose();
}
