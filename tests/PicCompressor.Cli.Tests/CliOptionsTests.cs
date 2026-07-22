namespace PicCompressor.Cli.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Parse_uses_safe_defaults()
    {
        var options = CliOptions.Parse(["input.png"]);

        Assert.Equal(80, options.Quality);
        Assert.Equal(PicCompressor.Domain.CollisionPolicy.Skip, options.CollisionPolicy);
        Assert.Equal(PicCompressor.Domain.LargerOutputPolicy.Discard, options.LargerOutputPolicy);
        Assert.Equal(PicCompressor.Domain.ExifPolicy.Remove, options.ExifPolicy);
        Assert.Equal(
            PicCompressor.Domain.ColorProfilePolicy.Preserve,
            options.ColorProfilePolicy);
    }

    [Fact]
    public void Parse_accepts_metadata_policies()
    {
        var options = CliOptions.Parse(
            ["input.png", "--exif", "private", "--color-profile", "srgb"]);

        Assert.Equal(PicCompressor.Domain.ExifPolicy.Private, options.ExifPolicy);
        Assert.Equal(
            PicCompressor.Domain.ColorProfilePolicy.Srgb,
            options.ColorProfilePolicy);
    }

    [Fact]
    public void Parse_rejects_unknown_metadata_policy()
    {
        var exception = Assert.Throws<CliUsageException>(
            () => CliOptions.Parse(["input.png", "--exif", "anonymise"]));

        Assert.Contains("--exif", exception.Message);
    }

    [Fact]
    public void Parse_rejects_invalid_quality()
    {
        var exception = Assert.Throws<CliUsageException>(
            () => CliOptions.Parse(["input.png", "--quality", "101"]));

        Assert.Contains("1 to 100", exception.Message);
    }

    [Fact]
    public void Parse_accepts_multiple_inputs()
    {
        var options = CliOptions.Parse(["first.png", "second.png"]);

        Assert.Equal(["first.png", "second.png"], options.InputPaths);
    }

    [Fact]
    public void Parse_accepts_batch_options()
    {
        var options = CliOptions.Parse(
            ["images", "--recursive", "--dry-run", "--parallelism", "3"]);

        Assert.True(options.Recursive);
        Assert.True(options.DryRun);
        Assert.Equal(3, options.Parallelism);
    }

    [Fact]
    public void Parse_defaults_timeout_to_no_limit()
    {
        var options = CliOptions.Parse(["input.png"]);

        Assert.Equal(0, options.TimeoutSeconds);
    }

    [Fact]
    public void Parse_accepts_a_runtime_timeout()
    {
        var options = CliOptions.Parse(["input.png", "--timeout", "120"]);

        Assert.Equal(120, options.TimeoutSeconds);
    }

    [Fact]
    public void Parse_rejects_an_out_of_range_timeout()
    {
        var exception = Assert.Throws<CliUsageException>(
            () => CliOptions.Parse(["input.png", "--timeout", "999999"]));

        Assert.Contains("--timeout", exception.Message);
    }
}
