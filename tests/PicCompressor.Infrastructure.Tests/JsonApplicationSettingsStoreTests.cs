namespace PicCompressor.Infrastructure.Tests;

public sealed class JsonApplicationSettingsStoreTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-settings-{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(directory, "config", "settings.json");

    [Fact]
    public void Load_returns_defaults_when_no_file_exists()
    {
        var settings = CreateStore().Load();

        Assert.Equal(new ApplicationSettings(), settings);
    }

    [Fact]
    public void Saved_settings_survive_a_round_trip()
    {
        var store = CreateStore();
        var settings = new ApplicationSettings
        {
            Language = "German",
            Theme = "Dark",
            Quality = 72,
            ChromaSubsampling = JpegliChromaSubsampling.Subsampling444,
            ProgressiveLevel = 0,
            ExifPolicy = ExifPolicy.Private,
            ColorProfilePolicy = ColorProfilePolicy.Srgb,
            CollisionPolicy = CollisionPolicy.Rename,
            LargerOutputPolicy = LargerOutputPolicy.Keep,
            Suffix = "_small",
            OutputDirectory = @"C:\out",
            ParallelJobs = 4,
            HistoryRetentionDays = 30,
            LogMaxFileMegabytes = 20,
            LogRetainedFiles = 3,
            JpegliTimeoutSeconds = 120,
            GuetzliTimeoutSeconds = 600,
            MinimumSavingsPercent = 12
        };

        store.Save(settings);

        Assert.Equal(settings, CreateStore().Load());
    }

    [Fact]
    public void Enums_are_stored_as_stable_names()
    {
        CreateStore().Save(new ApplicationSettings { ExifPolicy = ExifPolicy.Private });

        // Namen statt Zahlen: eine Umsortierung der Aufzählung darf eine
        // gespeicherte Konfiguration nicht umdeuten.
        Assert.Contains("\"Private\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_and_is_reported()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ this is not json");
        var log = new RecordingDiagnosticLog();

        var settings = new JsonApplicationSettingsStore(SettingsPath, log).Load();

        Assert.Equal(new ApplicationSettings(), settings);
        Assert.Contains(log.Entries, entry => entry.Severity is DiagnosticSeverity.Warning);
    }

    [Fact]
    public void A_newer_schema_version_falls_back_to_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "SchemaVersion": 99, "Quality": 10 }""");
        var log = new RecordingDiagnosticLog();

        var settings = new JsonApplicationSettingsStore(SettingsPath, log).Load();

        Assert.Equal(new ApplicationSettings().Quality, settings.Quality);
        Assert.Contains(log.Entries, entry => entry.Message.Contains("newer"));
    }

    [Theory]
    [InlineData("\"Quality\": 0")]
    [InlineData("\"Quality\": 101")]
    [InlineData("\"ProgressiveLevel\": 7")]
    [InlineData("\"ParallelJobs\": 0")]
    [InlineData("\"HistoryRetentionDays\": 0")]
    [InlineData("\"LogMaxFileMegabytes\": 0")]
    [InlineData("\"LogMaxFileMegabytes\": 5000")]
    [InlineData("\"LogRetainedFiles\": 0")]
    [InlineData("\"JpegliTimeoutSeconds\": -5")]
    [InlineData("\"GuetzliTimeoutSeconds\": 999999")]
    [InlineData("\"MinimumSavingsPercent\": -1")]
    [InlineData("\"MinimumSavingsPercent\": 100")]
    [InlineData("\"Suffix\": \"\"")]
    [InlineData("\"EngineId\": \"  \"")]
    public void An_out_of_range_field_falls_back_without_discarding_the_others(string field)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, $$"""{ "Theme": "Dark", {{field}} }""");
        var log = new RecordingDiagnosticLog();

        var settings = new JsonApplicationSettingsStore(SettingsPath, log).Load();
        var defaults = new ApplicationSettings();

        // Das gültige Feld bleibt erhalten, nur das unzulässige wird ersetzt.
        Assert.Equal("Dark", settings.Theme);
        Assert.InRange(settings.Quality, 1, 100);
        Assert.InRange(settings.ProgressiveLevel, 0, 2);
        Assert.InRange(settings.ParallelJobs, 1, 256);
        Assert.InRange(settings.HistoryRetentionDays, 1, 3650);
        Assert.InRange(settings.LogMaxFileMegabytes, 1, 1024);
        Assert.InRange(settings.LogRetainedFiles, 1, 100);
        Assert.InRange(settings.JpegliTimeoutSeconds, 0, 86_400);
        Assert.InRange(settings.GuetzliTimeoutSeconds, 0, 86_400);
        Assert.InRange(settings.MinimumSavingsPercent, 0, 99);
        Assert.False(string.IsNullOrWhiteSpace(settings.Suffix));
        Assert.False(string.IsNullOrWhiteSpace(settings.EngineId));
        Assert.Equal(defaults.SchemaVersion, settings.SchemaVersion);
        Assert.Contains(log.Entries, entry => entry.Message.Contains("replaced by defaults"));
    }

    [Fact]
    public void An_undefined_enum_value_falls_back_to_the_default()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "CollisionPolicy": 42 }""");

        var settings = new JsonApplicationSettingsStore(SettingsPath).Load();

        Assert.Equal(CollisionPolicy.Skip, settings.CollisionPolicy);
    }

    [Fact]
    public void Save_leaves_no_temporary_file_behind()
    {
        CreateStore().Save(new ApplicationSettings());

        Assert.True(File.Exists(SettingsPath));
        Assert.Empty(
            Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.tmp"));
    }

    [Fact]
    public void Save_does_not_throw_when_the_target_is_not_writable()
    {
        // Ein Verzeichnis anstelle der Datei macht das Ersetzen unmöglich.
        Directory.CreateDirectory(SettingsPath);
        var log = new RecordingDiagnosticLog();

        new JsonApplicationSettingsStore(SettingsPath, log).Save(new ApplicationSettings());

        Assert.Contains(log.Entries, entry => entry.Message.Contains("could not be saved"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private JsonApplicationSettingsStore CreateStore() => new(SettingsPath);

    private sealed class RecordingDiagnosticLog : IDiagnosticLog
    {
        public List<DiagnosticEntry> Entries { get; } = [];

        public void Write(DiagnosticEntry entry) => Entries.Add(entry);
    }
}
