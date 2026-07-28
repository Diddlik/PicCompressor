using PicCompressor.Domain;

namespace PicCompressor.Infrastructure.Tests;

/// <summary>
/// Die Konfigurationsdatei des Dauerbetriebs wird strikt gelesen (D-055): ein unbekanntes Feld,
/// ein ungültiger Wert oder eine nicht unterstützte Schemaversion ist ein Fehler statt eines still
/// eingesetzten Vorgabewerts.
/// </summary>
public sealed class JsonScanConfigurationStoreTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-config-{Guid.NewGuid():N}");

    public JsonScanConfigurationStoreTests() => Directory.CreateDirectory(directory);

    private JsonScanConfigurationStore Store(string json)
    {
        var path = Path.Combine(directory, "piccompressor.json");
        File.WriteAllText(path, json);
        return new(path);
    }

    [Fact]
    public void A_complete_configuration_is_read()
    {
        var configuration = Store(
            """
            {
              "schemaVersion": 1,
              "mode": "Watch",
              "intervalSeconds": 600,
              "stateDirectory": "/state",
              "history": true,
              "defaults": { "quality": 85, "exif": "private", "chromaSubsampling": "444" },
              "folders": [
                { "name": "fotos", "input": "/data/in", "output": "/data/out", "recursive": false }
              ]
            }
            """).Load();

        Assert.Equal(ScanMode.Watch, configuration.Mode);
        Assert.Equal(600, configuration.IntervalSeconds);
        Assert.True(configuration.History);
        // Enum-Namen werden unabhängig von der Schreibweise erkannt (Abschnitt 13.2).
        Assert.Equal(ExifPolicy.Private, configuration.Defaults!.Exif);
        Assert.Equal(JpegliChromaSubsampling.Subsampling444, configuration.Defaults.ChromaSubsampling);
        var folder = Assert.Single(configuration.Folders!);
        Assert.Equal("fotos", folder.Name);
        Assert.False(folder.Recursive);
    }

    [Fact]
    public void Chroma_subsampling_also_accepts_the_enum_name()
    {
        var configuration = Store(
            """
            {
              "schemaVersion": 1,
              "defaults": { "chromaSubsampling": "Subsampling420" },
              "folders": [ { "name": "a", "input": "/in", "output": "/out" } ]
            }
            """).Load();

        Assert.Equal(JpegliChromaSubsampling.Subsampling420, configuration.Defaults!.ChromaSubsampling);
    }

    [Fact]
    public void A_missing_file_is_reported()
    {
        var store = new JsonScanConfigurationStore(Path.Combine(directory, "absent.json"));

        Assert.Throws<FileNotFoundException>(() => store.Load());
    }

    [Fact]
    public void An_unknown_field_is_rejected()
    {
        // Ein Tippfehler im Feldnamen darf nicht als „nicht gesetzt" durchgehen.
        var store = Store(
            """
            {
              "schemaVersion": 1,
              "intervalSecond": 600,
              "folders": [ { "name": "a", "input": "/in", "output": "/out" } ]
            }
            """);

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void An_invalid_enum_value_is_rejected()
    {
        var store = Store(
            """
            {
              "schemaVersion": 1,
              "defaults": { "exif": "maybe" },
              "folders": [ { "name": "a", "input": "/in", "output": "/out" } ]
            }
            """);

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void An_invalid_chroma_subsampling_is_rejected()
    {
        var store = Store(
            """
            {
              "schemaVersion": 1,
              "defaults": { "chromaSubsampling": "411" },
              "folders": [ { "name": "a", "input": "/in", "output": "/out" } ]
            }
            """);

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void A_newer_schema_version_is_rejected()
    {
        var store = Store("""{ "schemaVersion": 2, "folders": [] }""");

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void A_missing_schema_version_is_rejected()
    {
        var store = Store("""{ "folders": [] }""");

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void Broken_json_is_rejected()
    {
        var store = Store("{ not json");

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
