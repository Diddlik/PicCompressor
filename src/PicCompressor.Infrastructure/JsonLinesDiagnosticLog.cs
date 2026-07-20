using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicCompressor.Application;

namespace PicCompressor.Infrastructure;

/// <summary>
/// Strukturiertes Log im JSON-Lines-Format (Abschnitt 13.3). Jede Zeile ist ein
/// eigenständiges JSON-Objekt, damit ein abgeschnittener Schreibvorgang die
/// übrigen Zeilen nicht unlesbar macht.
/// </summary>
/// <remarks>
/// Rotation und Aufbewahrung sind über <paramref name="maxFileBytes"/> und
/// <paramref name="maxFiles"/> konfigurierbar. Schreibfehler werden bewusst
/// verschluckt: ein nicht schreibbares Log darf keine Kompression scheitern
/// lassen.
/// </remarks>
public sealed class JsonLinesDiagnosticLog : IDiagnosticLog
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string path;
    private readonly long maxFileBytes;
    private readonly int maxFiles;
    private readonly Lock gate = new();

    public JsonLinesDiagnosticLog(
        string path,
        long maxFileBytes = 5 * 1024 * 1024,
        int maxFiles = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);

        this.path = Path.GetFullPath(path);
        this.maxFileBytes = maxFileBytes;
        this.maxFiles = maxFiles;
    }

    public void Write(DiagnosticEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var line = JsonSerializer.Serialize(entry, SerializerOptions);
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RotateIfNeeded(Encoding.UTF8.GetByteCount(line) + 1);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Ein nicht schreibbares Log ist kein Produktfehler (Abschnitt 13.3).
            }
        }
    }

    private void RotateIfNeeded(int pendingBytes)
    {
        var current = new FileInfo(path);
        if (!current.Exists || current.Length + pendingBytes <= maxFileBytes)
        {
            return;
        }

        // Die älteste Generation fällt heraus, die übrigen rücken auf.
        var oldest = Rotated(maxFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var generation = maxFiles - 2; generation >= 1; generation--)
        {
            var source = Rotated(generation);
            if (File.Exists(source))
            {
                File.Move(source, Rotated(generation + 1), overwrite: true);
            }
        }

        File.Move(path, Rotated(1), overwrite: true);
    }

    private string Rotated(int generation) => $"{path}.{generation}";
}
