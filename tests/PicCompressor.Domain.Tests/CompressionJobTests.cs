using PicCompressor.Domain;

namespace PicCompressor.Domain.Tests;

public sealed class CompressionJobTests
{
    [Fact]
    public void Constructor_keeps_typed_engine_settings()
    {
        var settings = new JpegliSettings(
            80,
            JpegliChromaSubsampling.Subsampling420,
            2);

        var job = CreateJob(settings);

        Assert.Same(settings, job.EngineSettings);
    }

    [Fact]
    public void Constructor_rejects_empty_job_id()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateJob(
                new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
                Guid.Empty));

        Assert.Equal("id", exception.ParamName);
    }

    private static CompressionJob CreateJob(
        CompressionEngineSettings settings,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            Path.GetFullPath("input.png"),
            Path.GetFullPath("input_compressed.jpg"),
            settings,
            ExifPolicy.Private,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Discard,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, 1, 1, 1));
}
