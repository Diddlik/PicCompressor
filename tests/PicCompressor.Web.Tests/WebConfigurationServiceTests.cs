using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Web;

namespace PicCompressor.Web.Tests;

public sealed class WebConfigurationServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"piccompressor-web-{Guid.NewGuid():N}");

    public WebConfigurationServiceTests()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(StateRoot);
        Directory.CreateDirectory(ConfigRoot);
        Directory.CreateDirectory(Path.Combine(DataRoot, "photos"));
        Directory.CreateDirectory(Path.Combine(DataRoot, "output"));
    }

    private string DataRoot => Path.Combine(root, "data");
    private string StateRoot => Path.Combine(root, "state");
    private string ConfigRoot => Path.Combine(root, "config");
    private string ConfigPath => Path.Combine(ConfigRoot, "piccompressor.json");

    [Fact]
    public void A_valid_configuration_is_saved_and_gets_a_new_revision()
    {
        var service = new WebConfigurationService(ConfigPath, DataRoot, StateRoot);
        var initial = service.CreateDefault();
        var configuration = ValidConfiguration();

        var saved = service.Save(configuration, initial.Revision);

        Assert.NotEqual(initial.Revision, saved.Revision);
        Assert.Equal(300, saved.Configuration.IntervalSeconds);
        Assert.True(File.Exists(ConfigPath));
    }

    [Fact]
    public void A_path_outside_the_data_root_is_rejected_without_replacing_the_file()
    {
        var service = new WebConfigurationService(ConfigPath, DataRoot, StateRoot);
        var initial = service.Save(ValidConfiguration(), service.CreateDefault().Revision);
        var invalid = ValidConfiguration() with
        {
            Folders = [new(Name: "escape", Input: "/etc", Output: Path.Combine(DataRoot, "output"))]
        };

        var exception = Assert.Throws<ConfigurationValidationException>(
            () => service.Save(invalid, initial.Revision));

        Assert.Contains(exception.Errors, error => error.Contains("/data", StringComparison.Ordinal));
        Assert.Equal(initial.Revision, service.Load().Revision);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("state")]
    [InlineData("lock")]
    [InlineData("log")]
    public void A_symlink_cannot_escape_the_configured_roots(string pathKind)
    {
        var service = new WebConfigurationService(ConfigPath, DataRoot, StateRoot);
        var outside = Path.Combine(root, "outside", pathKind);
        Directory.CreateDirectory(outside);
        var mountedRoot = pathKind is "input" or "output" ? DataRoot : StateRoot;
        var link = Path.Combine(mountedRoot, $"{pathKind}-escape");
        Directory.CreateSymbolicLink(link, outside);
        var configuration = ValidConfiguration();
        var folder = configuration.Folders!.Single();
        configuration = pathKind switch
        {
            "input" => configuration with { Folders = [folder with { Input = link }] },
            "output" => configuration with { Folders = [folder with { Output = link }] },
            "state" => configuration with
            {
                Folders = [folder with { StatePath = Path.Combine(link, "scan.json") }]
            },
            "lock" => configuration with { LockPath = Path.Combine(link, "scan.lock") },
            "log" => configuration with { LogPath = Path.Combine(link, "scan.jsonl") },
            _ => throw new ArgumentOutOfRangeException(nameof(pathKind))
        };

        var exception = Assert.Throws<ConfigurationValidationException>(
            () => service.Validate(configuration));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("symbolic link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_symlink_is_rejected_even_when_its_current_target_is_inside_the_data_root()
    {
        var service = new WebConfigurationService(ConfigPath, DataRoot, StateRoot);
        var realInput = Path.Combine(DataRoot, "real-input");
        Directory.CreateDirectory(realInput);
        var link = Path.Combine(DataRoot, "linked-input");
        Directory.CreateSymbolicLink(link, realInput);
        var folder = ValidConfiguration().Folders!.Single();
        var configuration = ValidConfiguration() with
        {
            Folders = [folder with { Input = link }]
        };

        var exception = Assert.Throws<ConfigurationValidationException>(
            () => service.Validate(configuration));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("symbolic link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_dangling_symlink_cannot_be_approved_for_a_future_escape()
    {
        var service = new WebConfigurationService(ConfigPath, DataRoot, StateRoot);
        var outside = Path.Combine(root, "outside", "created-later");
        var link = Path.Combine(DataRoot, "dangling-escape");
        Directory.CreateSymbolicLink(link, outside);
        var folder = ValidConfiguration().Folders!.Single();
        var configuration = ValidConfiguration() with
        {
            Folders = [folder with { Input = Path.Combine(link, "photos") }]
        };

        var exception = Assert.Throws<ConfigurationValidationException>(
            () => service.Validate(configuration));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("symbolic link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_dangling_file_symlink_cannot_escape_the_state_root()
    {
        var service = new WebConfigurationService(ConfigPath, DataRoot, StateRoot);
        var outside = Path.Combine(root, "outside", "created-later.lock");
        var link = Path.Combine(StateRoot, "dangling.lock");
        File.CreateSymbolicLink(link, outside);
        var configuration = ValidConfiguration() with { LockPath = link };

        var exception = Assert.Throws<ConfigurationValidationException>(
            () => service.Validate(configuration));

        Assert.Contains(
            exception.Errors,
            error => error.Contains("symbolic link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_stale_browser_revision_is_rejected()
    {
        var service = new WebConfigurationService(ConfigPath, DataRoot, StateRoot);
        var initial = service.CreateDefault();
        var saved = service.Save(ValidConfiguration(), initial.Revision);

        Assert.Throws<ConfigurationConflictException>(
            () => service.Save(ValidConfiguration() with { IntervalSeconds = 600 }, initial.Revision));
        Assert.Equal(saved.Revision, service.Load().Revision);
    }

    private ScanConfiguration ValidConfiguration() => new(
        SchemaVersion: 1,
        Mode: ScanMode.Interval,
        IntervalSeconds: 300,
        DebounceSeconds: 10,
        StableForSeconds: 120,
        Parallelism: 1,
        TimeoutSeconds: 300,
        StateDirectory: StateRoot,
        LockPath: Path.Combine(StateRoot, "piccompressor.lock"),
        LogPath: Path.Combine(StateRoot, "piccompressor.jsonl"),
        History: false,
        Defaults: new(
            Quality: 80,
            ChromaSubsampling: JpegliChromaSubsampling.Subsampling420,
            ProgressiveLevel: 2,
            Exif: ExifPolicy.Remove,
            ColorProfile: ColorProfilePolicy.Preserve,
            AlphaBackground: "#FFFFFF",
            Collision: CollisionPolicy.Skip,
            LargerOutput: LargerOutputPolicy.Discard,
            MinSavingsPercent: 5,
            Suffix: "_compressed"),
        Folders:
        [
            new(
                Name: "photos",
                Input: Path.Combine(DataRoot, "photos"),
                Output: Path.Combine(DataRoot, "output"),
                Recursive: true)
        ]);

    public void Dispose() => Directory.Delete(root, recursive: true);
}
