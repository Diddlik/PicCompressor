namespace PicCompressor.Infrastructure.Tests;

public sealed class JsonCompressionProfileStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"piccompressor-profiles-{Guid.NewGuid():N}");
    private string ProfilePath => Path.Combine(directory, "profiles.json");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Profiles_round_trip_including_alpha_and_output_settings(bool overwriteOriginal)
    {
        var profile = new CompressionProfile
        {
            Name = "Blog", Quality = 73, ChromaSubsampling = JpegliChromaSubsampling.Subsampling444,
            ProgressiveLevel = 0, AlphaBackground = new(12, 34, 56),
            OutputDirectory = overwriteOriginal ? null : directory,
            OverwriteOriginal = overwriteOriginal,
            CollisionPolicy = overwriteOriginal ? CollisionPolicy.Overwrite : CollisionPolicy.Skip,
            ExifPolicy = ExifPolicy.Private, ColorProfilePolicy = ColorProfilePolicy.Srgb,
            ParallelJobs = 3, JpegliTimeoutSeconds = 45, MinimumSavingsPercent = 5
        };
        new JsonCompressionProfileStore(ProfilePath).Save([profile]);
        Assert.Equal(profile, Assert.Single(new JsonCompressionProfileStore(ProfilePath).Load()));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Legacy_profiles_do_not_enable_replacing_originals()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(ProfilePath, """{"SchemaVersion":1,"Profiles":[{"Name":"Legacy"}]}""");
        Assert.False(Assert.Single(new JsonCompressionProfileStore(ProfilePath).Load()).OverwriteOriginal);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"SchemaVersion\":2,\"Profiles\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Profiles\":[null]}")]
    public void Invalid_documents_cannot_be_overwritten(string contents)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(ProfilePath, contents);
        Assert.ThrowsAny<Exception>(() => new JsonCompressionProfileStore(ProfilePath).Save([]));
        Assert.Equal(contents, File.ReadAllText(ProfilePath));
    }

    [Fact]
    public void Duplicate_names_and_invalid_settings_preserve_existing_file()
    {
        var store = new JsonCompressionProfileStore(ProfilePath);
        var profile = new CompressionProfile { Name = "Blog" };
        store.Save([profile]);
        var original = File.ReadAllText(ProfilePath);
        Assert.Throws<InvalidDataException>(() => store.Save([profile, profile with { Name = "blog" }]));
        Assert.ThrowsAny<ArgumentException>(() => store.Save([profile with { Quality = 0 }]));
        Assert.ThrowsAny<ArgumentException>(() => store.Save([profile with { Suffix = "../outside" }]));
        Assert.Equal(original, File.ReadAllText(ProfilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
    }
}
