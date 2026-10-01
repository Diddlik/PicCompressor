using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

/// <summary>
/// Prüfung und Zusammenführung der Konfigurationsdatei des Dauerbetriebs (D-055): ein Ordner erbt
/// die globalen Vorgaben und überschreibt einzelne Werte; ein unzulässiger Wert ist ein Fehler und
/// wird nicht still ersetzt.
/// </summary>
public sealed class ScanConfigurationTests
{
    private const string StateDirectory = "/state";

    private static ScanConfigurationResolution Resolve(ScanConfiguration configuration) =>
        ScanConfigurationResolver.Resolve(configuration, StateDirectory, StringComparer.Ordinal);

    private static ScanConfiguration WithFolders(params ScanFolderConfiguration[] folders) =>
        new(ScanConfiguration.CurrentSchemaVersion, Folders: folders);

    private static ScanFolderConfiguration Folder(
        string name = "fotos",
        string? input = "/data/in",
        string? output = "/data/out",
        ScanCompressionSettings? settings = null) =>
        new(name, input, output, Settings: settings);

    [Fact]
    public void A_folder_inherits_the_defaults_and_overrides_single_values()
    {
        var resolution = Resolve(
            new(
                ScanConfiguration.CurrentSchemaVersion,
                Defaults: new(Quality: 70, Exif: ExifPolicy.Keep, MinSavingsPercent: 5),
                Folders:
                [
                    Folder("fotos"),
                    Folder("scans", "/data/scans", "/data/scans-out", new(Quality: 92))
                ]));

        var configuration = Assert.IsType<ResolvedScanConfiguration>(resolution.Configuration);
        Assert.Empty(resolution.Errors);
        var photos = configuration.Folders[0];
        var scans = configuration.Folders[1];
        Assert.Equal(70, photos.Settings.EngineSettings.Quality);
        Assert.Equal(92, scans.Settings.EngineSettings.Quality);
        // Nicht überschriebene Werte bleiben die globalen Vorgaben.
        Assert.Equal(ExifPolicy.Keep, scans.Settings.ExifPolicy);
        Assert.Equal(5, scans.Settings.MinimumSavingsPercent);
    }

    [Fact]
    public void Unset_values_fall_back_to_the_product_defaults()
    {
        var configuration = Resolve(WithFolders(Folder())).Configuration!;

        Assert.Equal(ScanMode.Interval, configuration.Mode);
        Assert.Equal(TimeSpan.FromSeconds(900), configuration.Interval);
        Assert.False(configuration.History);
        var folder = configuration.Folders[0];
        Assert.True(folder.Recursive);
        Assert.Equal(80, folder.Settings.EngineSettings.Quality);
        Assert.Equal(ExifPolicy.Remove, folder.Settings.ExifPolicy);
        Assert.Equal(CollisionPolicy.Skip, folder.Settings.CollisionPolicy);
        Assert.Equal(RgbColor.White, folder.Settings.AlphaBackground);
        Assert.Equal("_compressed", folder.Settings.Suffix);
    }

    [Fact]
    public void The_state_path_defaults_to_the_folder_name_in_the_state_directory()
    {
        var configuration = Resolve(WithFolders(Folder("fotos"))).Configuration!;

        Assert.Equal(
            Path.GetFullPath(Path.Combine(StateDirectory, "fotos.json")),
            configuration.Folders[0].StatePath);
    }

    [Fact]
    public void An_explicit_state_path_wins_over_the_default()
    {
        var configuration = Resolve(
            WithFolders(
                new ScanFolderConfiguration(
                    "fotos", "/data/in", "/data/out", StatePath: "/var/fotos.json")))
            .Configuration!;

        Assert.Equal(Path.GetFullPath("/var/fotos.json"), configuration.Folders[0].StatePath);
    }

    [Fact]
    public void A_configuration_without_folders_is_rejected()
    {
        var resolution = Resolve(new(ScanConfiguration.CurrentSchemaVersion));

        Assert.Null(resolution.Configuration);
        Assert.Contains(resolution.Errors, error => error.Contains("folders", StringComparison.Ordinal));
    }

    [Fact]
    public void A_folder_without_input_or_output_is_rejected()
    {
        var resolution = Resolve(WithFolders(Folder(input: null, output: null)));

        Assert.Null(resolution.Configuration);
        Assert.Equal(2, resolution.Errors.Count);
    }

    [Fact]
    public void An_output_equal_to_the_input_is_rejected()
    {
        // Originale zu ersetzen ist nach 7.2 eine ausdrückliche Auswahl, nicht die Nebenwirkung
        // zweier gleicher Pfade.
        var resolution = Resolve(WithFolders(Folder(input: "/data/in", output: "/data/in")));

        Assert.Null(resolution.Configuration);
        Assert.Contains(resolution.Errors, error => error.Contains("differ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_folder_that_replaces_its_originals_needs_no_output_and_forces_overwrite()
    {
        var resolution = Resolve(
            new ScanConfiguration(
                ScanConfiguration.CurrentSchemaVersion,
                Defaults: new(Collision: CollisionPolicy.Skip),
                Folders: [new("fotos", "/data/in", OverwriteOriginal: true)]));

        var folder = Assert.Single(resolution.Configuration!.Folders);
        Assert.Null(folder.OutputDirectory);
        Assert.True(folder.Settings.OverwriteOriginal);
        Assert.Null(folder.Settings.OutputDirectory);
        // Die Originalersetzung ist die ausdrückliche Freigabe; ein globales `skip` gilt hier nicht.
        Assert.Equal(CollisionPolicy.Overwrite, folder.Settings.CollisionPolicy);
    }

    [Fact]
    public void A_folder_that_replaces_its_originals_rejects_an_output()
    {
        var resolution = Resolve(
            WithFolders(new ScanFolderConfiguration("fotos", "/data/in", "/data/out", OverwriteOriginal: true)));

        Assert.Null(resolution.Configuration);
        Assert.Contains(resolution.Errors, error => error.Contains("omitted", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("sub/folder")]
    public void A_name_that_is_not_a_plain_file_name_is_rejected(string name)
    {
        var resolution = Resolve(WithFolders(Folder(name)));

        Assert.Null(resolution.Configuration);
        Assert.Contains(resolution.Errors, error => error.Contains("name", StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicate_name_is_rejected()
    {
        // Zwei gleiche Namen teilten sich sonst eine Zustandsdatei.
        var resolution = Resolve(
            WithFolders(Folder("fotos"), Folder("Fotos", "/data/other", "/data/other-out")));

        Assert.Null(resolution.Configuration);
        Assert.Contains(resolution.Errors, error => error.Contains("unique", StringComparison.Ordinal));
    }

    [Fact]
    public void Values_outside_their_range_are_rejected_instead_of_clamped()
    {
        var resolution = ScanConfigurationResolver.Resolve(
            new(
                ScanConfiguration.CurrentSchemaVersion,
                IntervalSeconds: 0,
                Parallelism: 500,
                Defaults: new(Quality: 101),
                Folders: [Folder()]),
            StateDirectory,
            StringComparer.Ordinal);

        Assert.Null(resolution.Configuration);
        Assert.Equal(3, resolution.Errors.Count);
    }

    [Fact]
    public void An_invalid_alpha_background_is_rejected()
    {
        var resolution = Resolve(
            WithFolders(Folder(settings: new(AlphaBackground: "white"))));

        Assert.Null(resolution.Configuration);
        Assert.Contains(
            resolution.Errors,
            error => error.Contains("alphaBackground", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hexadecimal_alpha_background_is_taken_over()
    {
        var configuration = Resolve(
            WithFolders(Folder(settings: new(AlphaBackground: "#102030")))).Configuration!;

        Assert.Equal(new RgbColor(0x10, 0x20, 0x30), configuration.Folders[0].Settings.AlphaBackground);
    }

    [Fact]
    public void Two_folders_with_different_settings_get_different_fingerprints()
    {
        // Der Fingerabdruck entscheidet, ob ein Lauf erneut kodiert (D-051); er muss die
        // ordnerspezifischen Einstellungen enthalten.
        var configuration = Resolve(
            WithFolders(
                Folder("fotos"),
                Folder("scans", "/data/scans", "/data/scans-out", new(Quality: 92))))
            .Configuration!;

        Assert.NotEqual(
            IncrementalScan.Fingerprint(configuration.Folders[0].Settings),
            IncrementalScan.Fingerprint(configuration.Folders[1].Settings));
    }
}
