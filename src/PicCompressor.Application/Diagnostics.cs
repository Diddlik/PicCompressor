using PicCompressor.Domain;

namespace PicCompressor.Application;

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Ein strukturierter Logeintrag nach Abschnitt 13.3. Er trägt Zeit, Schweregrad,
/// Komponente sowie – sofern zutreffend – Job-ID und Fehlerkategorie.
/// </summary>
/// <remarks>
/// <paramref name="Message"/> und <paramref name="FileName"/> dürfen keine Bilddaten,
/// Metadateninhalte oder vollständigen Pfade enthalten. Aufrufer übergeben den
/// Dateinamen, nicht den Pfad.
/// </remarks>
public sealed record DiagnosticEntry(
    DateTimeOffset Timestamp,
    DiagnosticSeverity Severity,
    string Component,
    string Message,
    string? FileName = null,
    Guid? JobId = null,
    CompressionErrorCategory? ErrorCategory = null);

public interface IDiagnosticLog
{
    void Write(DiagnosticEntry entry);
}

/// <summary>Verwirft alle Einträge; für Tests und für abgeschaltetes Logging.</summary>
public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static NullDiagnosticLog Instance { get; } = new();

    public void Write(DiagnosticEntry entry)
    {
    }
}
