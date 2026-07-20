using System.Text.Json;

namespace PicCompressor.Cli.Tests;

public sealed class CliApplicationTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"piccompressor-cli-{Guid.NewGuid():N}");

    public CliApplicationTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, "input.png"),
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
    }

    [Fact]
    public async Task RunAsync_dry_run_plans_folder_without_writing_output()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            [directory, "--dry-run", "--json"],
            output,
            error);

        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, exitCode);
        Assert.True(json.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("plans").EnumerateArray());
        Assert.Empty(Directory.GetFiles(directory, "*.jpg"));
        Assert.Equal("", error.ToString());
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
