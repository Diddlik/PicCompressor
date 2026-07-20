namespace PicCompressor.Infrastructure.Tests;

public sealed class SafeOutputPublisherIntegrationTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"PicCompressor-{Guid.NewGuid():N}");

    public SafeOutputPublisherIntegrationTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void Publish_moves_validated_jpeg_and_removes_temporary_file()
    {
        var job = CreateJob(CollisionPolicy.Skip);
        var publisher = CreatePublisher();
        var temporaryOutput = publisher.CreateTemporaryFile(job);
        File.WriteAllBytes(temporaryOutput.Path, CreateJpeg(10, 10));

        var result = publisher.Publish(job, temporaryOutput);

        Assert.Equal(OutputPublicationDisposition.Published, result.Disposition);
        Assert.True(File.Exists(job.OutputPath));
        Assert.False(File.Exists(temporaryOutput.Path));
    }

    [Fact]
    public void Invalid_output_preserves_existing_target_and_is_cleaned_up()
    {
        var job = CreateJob(CollisionPolicy.Overwrite);
        var originalTarget = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(job.OutputPath, originalTarget);
        var publisher = CreatePublisher();
        var temporaryOutput = publisher.CreateTemporaryFile(job);
        File.WriteAllBytes(temporaryOutput.Path, [1, 2, 3, 4, 5, 6, 7, 8]);

        var exception = Assert.Throws<OutputPublicationException>(
            () => publisher.Publish(job, temporaryOutput));

        Assert.Equal(CompressionErrorCategory.OutputValidationFailed, exception.Category);
        Assert.Equal(originalTarget, File.ReadAllBytes(job.OutputPath));
        Assert.False(File.Exists(temporaryOutput.Path));
    }

    public void Dispose()
    {
        Directory.Delete(directory, true);
        GC.SuppressFinalize(this);
    }

    private SafeOutputPublisher CreatePublisher() =>
        new(
            new PhysicalFileSystem(StringComparer.OrdinalIgnoreCase),
            new PhysicalInputImageInspector());

    private CompressionJob CreateJob(CollisionPolicy collisionPolicy) =>
        new(
            Guid.NewGuid(),
            Path.Combine(directory, "input.png"),
            Path.Combine(directory, "output.jpg"),
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Private,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            collisionPolicy,
            LargerOutputPolicy.Keep,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, 10, 10, 1_000));

    private static byte[] CreateJpeg(int width, int height) =>
    [
        0xff, 0xd8,
        0xff, 0xc0, 0x00, 0x0b, 0x08,
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0x01, 0x01, 0x11, 0x00,
        0xff, 0xda, 0x00, 0x08,
        0x01, 0x01, 0x00, 0x00, 0x3f, 0x00,
        0x11, 0x22,
        0xff, 0xd9
    ];
}
