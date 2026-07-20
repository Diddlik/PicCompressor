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
}
