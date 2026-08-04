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
        File.WriteAllBytes(temporaryOutput.Path, TestJpeg.Create(10, 10));

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

    [Fact]
    public void Publish_accepts_the_upright_output_of_a_rotated_jpeg_input()
    {
        // 8.2: der Encoder dreht die Pixel aufrecht, die Ausgabe hat also getauschte Achsen.
        // Vor dem Fix verwarf die Dimensionsprüfung jedes gedrehte Foto.
        var inputPath = Path.Combine(directory, "rotated.jpg");
        File.WriteAllBytes(inputPath, TestJpeg.Create(17, 9, orientation: 6));
        var inputInfo = new PhysicalInputImageInspector().Inspect(inputPath);
        var job = CreateJob(CollisionPolicy.Skip, inputPath, inputInfo);
        var publisher = CreatePublisher();
        var temporaryOutput = publisher.CreateTemporaryFile(job);
        File.WriteAllBytes(temporaryOutput.Path, TestJpeg.Create(9, 17));

        var result = publisher.Publish(job, temporaryOutput);

        Assert.Equal(OutputPublicationDisposition.Published, result.Disposition);
        Assert.True(File.Exists(job.OutputPath));
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

    private CompressionJob CreateJob(
        CollisionPolicy collisionPolicy,
        string? inputPath = null,
        InputImageInfo? inputImageInfo = null) =>
        new(
            Guid.NewGuid(),
            inputPath ?? Path.Combine(directory, "input.png"),
            Path.Combine(directory, "output.jpg"),
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Private,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            collisionPolicy,
            LargerOutputPolicy.Keep,
            DateTimeOffset.UtcNow,
            inputImageInfo ?? new InputImageInfo(InputImageFormat.Png, 10, 10, 1_000));
}
