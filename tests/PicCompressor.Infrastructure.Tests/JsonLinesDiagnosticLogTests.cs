using System.Text.Json;

namespace PicCompressor.Infrastructure.Tests;

public sealed class JsonLinesDiagnosticLogTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-log-{Guid.NewGuid():N}");

    private string LogPath => Path.Combine(directory, "logs", "piccompressor.jsonl");

    [Fact]
    public void Write_appends_one_self_contained_json_object_per_entry()
    {
        var log = new JsonLinesDiagnosticLog(LogPath);

        log.Write(Entry("first"));
        log.Write(Entry("second"));

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("Cli", document.RootElement.GetProperty("Component").GetString());
        }
    }

    [Fact]
    public void Write_serialises_severity_and_error_category_as_stable_names()
    {
        var log = new JsonLinesDiagnosticLog(LogPath);

        log.Write(
            new DiagnosticEntry(
                DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
                DiagnosticSeverity.Error,
                "Cli",
                "failed",
                "image.png",
                Guid.Empty,
                CompressionErrorCategory.EngineFailed));

        using var document = JsonDocument.Parse(File.ReadAllLines(LogPath)[0]);
        // Fehlerkategorien sind stabile Bezeichner und bleiben unübersetzt
        // (Abschnitt 6.4).
        Assert.Equal("Error", document.RootElement.GetProperty("Severity").GetString());
        Assert.Equal(
            "EngineFailed",
            document.RootElement.GetProperty("ErrorCategory").GetString());
    }

    [Fact]
    public void Write_omits_absent_optional_fields()
    {
        var log = new JsonLinesDiagnosticLog(LogPath);

        log.Write(Entry("no job attached"));

        using var document = JsonDocument.Parse(File.ReadAllLines(LogPath)[0]);
        Assert.False(document.RootElement.TryGetProperty("JobId", out _));
        Assert.False(document.RootElement.TryGetProperty("ErrorCategory", out _));
    }

    [Fact]
    public void Write_rotates_and_keeps_at_most_the_configured_number_of_files()
    {
        var log = new JsonLinesDiagnosticLog(LogPath, maxFileBytes: 1024, maxFiles: 3);

        for (var index = 0; index < 200; index++)
        {
            log.Write(Entry($"entry {index} {new string('x', 100)}"));
        }

        Assert.True(File.Exists(LogPath));
        Assert.True(File.Exists($"{LogPath}.1"));
        Assert.True(File.Exists($"{LogPath}.2"));
        // maxFiles begrenzt die Gesamtzahl der Generationen.
        Assert.False(File.Exists($"{LogPath}.3"));
        Assert.True(new FileInfo(LogPath).Length <= 1024);
    }

    [Fact]
    public void Write_does_not_throw_when_the_target_is_not_writable()
    {
        // Ein Verzeichnis anstelle der Logdatei macht das Schreiben unmöglich.
        Directory.CreateDirectory(LogPath);
        var log = new JsonLinesDiagnosticLog(LogPath);

        log.Write(Entry("unwritable"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DiagnosticEntry Entry(string message) =>
        new(
            DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
            DiagnosticSeverity.Information,
            "Cli",
            message);
}
