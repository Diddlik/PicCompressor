using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Infrastructure;
using System.Text.Json;

namespace PicCompressor.Cli.Tests;

public sealed class ProfileCliTests
{
    [Fact]
    public async Task Saved_profile_reaches_the_real_dry_run_output_plan()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"piccompressor-profile-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "input.png");
            File.WriteAllBytes(input, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            var store = new JsonCompressionProfileStore(Path.Combine(directory, "profiles.json"));
            store.Save([new CompressionProfile { Name = "Blog", Suffix = "_blog" }]);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = await CliApplication.RunAsync([input, "--profile", "blog", "--dry-run", "--json", "--no-history"],
                output, error, diagnosticLog: NullDiagnosticLog.Instance, profileStore: store);
            Assert.Equal(0, exit);
            Assert.Equal("", error.ToString());
            using var document = JsonDocument.Parse(output.ToString());
            var plan = Assert.Single(document.RootElement.GetProperty("plans").EnumerateArray());
            Assert.EndsWith("input_blog.jpg", plan.GetProperty("outputPath").GetString());
            Assert.Empty(Directory.GetFiles(directory, "*.jpg"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_options_override_profile_independently_of_order(bool profileFirst)
    {
        var profile = new CompressionProfile
        {
            Name = "Blog", Quality = 73, ChromaSubsampling = JpegliChromaSubsampling.Subsampling444,
            ProgressiveLevel = 0, MinimumSavingsPercent = 5, AlphaBackground = new(12, 34, 56)
        };
        var args = profileFirst
            ? new[] { "input.png", "--profile", "Blog", "--quality", "60" }
            : new[] { "input.png", "--quality", "60", "--profile", "Blog" };
        var options = CliOptions.Parse(args, profile);
        Assert.Equal(60, options.Quality);
        Assert.Equal(profile.ChromaSubsampling, options.ChromaSubsampling);
        Assert.Equal(0, options.ProgressiveLevel);
        Assert.Equal(profile.AlphaBackground, options.AlphaBackground);
        Assert.Equal(5, options.MinimumSavingsPercent);
    }

    [Fact]
    public void Profiles_cannot_be_mixed_with_scan_configuration() =>
        Assert.Throws<CliUsageException>(() => CliOptions.Parse(["--config", "scan.json", "--profile", "Blog"]));

    [Fact]
    public async Task Missing_profile_returns_usage_error_before_processing()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await CliApplication.RunAsync(["missing.png", "--profile", "absent", "--json"], output, error,
            profileStore: new InMemoryCompressionProfileStore());
        Assert.Equal(2, exit);
        Assert.Contains("InvalidArguments", error.ToString());
        Assert.Equal("", output.ToString());
    }
}
