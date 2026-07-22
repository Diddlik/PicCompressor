using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

public sealed class SafeOutputPublisherTests
{
    [Fact]
    public void Publish_discards_output_that_is_not_smaller()
    {
        var fileSystem = new StubOutputFileSystem("temp.tmp");
        var publisher = CreatePublisher(fileSystem, fileSize: 100);
        var job = CreateJob(inputSize: 100);
        var temporaryOutput = publisher.CreateTemporaryFile(job);

        var result = publisher.Publish(job, temporaryOutput);

        Assert.Equal(OutputPublicationDisposition.DiscardedNotSmaller, result.Disposition);
        Assert.False(fileSystem.FileExists(fileSystem.Path("temp.tmp")));
        Assert.False(fileSystem.FileExists(fileSystem.Path("output.jpg")));
    }

    [Fact]
    public void Publish_keeps_larger_output_when_requested()
    {
        var fileSystem = new StubOutputFileSystem("temp.tmp");
        var publisher = CreatePublisher(fileSystem, fileSize: 101);
        var job = CreateJob(inputSize: 100, largerOutputPolicy: LargerOutputPolicy.Keep);
        var temporaryOutput = publisher.CreateTemporaryFile(job);

        var result = publisher.Publish(job, temporaryOutput);

        Assert.Equal(OutputPublicationDisposition.Published, result.Disposition);
        Assert.True(fileSystem.FileExists(fileSystem.Path("output.jpg")));
        Assert.False(fileSystem.FileExists(fileSystem.Path("temp.tmp")));
    }

    [Fact]
    public void Publish_discards_output_below_the_minimum_savings()
    {
        // 100-Byte-Eingabe, 60-Byte-Ausgabe = 40 % Einsparung; die Grenze fordert 50 %.
        var fileSystem = new StubOutputFileSystem("temp.tmp");
        var publisher = CreatePublisher(fileSystem, fileSize: 60);
        var job = CreateJob(inputSize: 100, minimumSavingsPercent: 50);
        var temporaryOutput = publisher.CreateTemporaryFile(job);

        var result = publisher.Publish(job, temporaryOutput);

        Assert.Equal(OutputPublicationDisposition.DiscardedBelowMinimumSavings, result.Disposition);
        Assert.False(fileSystem.FileExists(fileSystem.Path("temp.tmp")));
        Assert.False(fileSystem.FileExists(fileSystem.Path("output.jpg")));
    }

    [Fact]
    public void Publish_keeps_output_meeting_the_minimum_savings()
    {
        // 100-Byte-Eingabe, 40-Byte-Ausgabe = 60 % Einsparung; die Grenze fordert 50 %.
        var fileSystem = new StubOutputFileSystem("temp.tmp");
        var publisher = CreatePublisher(fileSystem, fileSize: 40);
        var job = CreateJob(inputSize: 100, minimumSavingsPercent: 50);
        var temporaryOutput = publisher.CreateTemporaryFile(job);

        var result = publisher.Publish(job, temporaryOutput);

        Assert.Equal(OutputPublicationDisposition.Published, result.Disposition);
        Assert.True(fileSystem.FileExists(fileSystem.Path("output.jpg")));
    }

    [Fact]
    public void Publish_removes_invalid_temporary_output()
    {
        var fileSystem = new StubOutputFileSystem("temp.tmp");
        var publisher = CreatePublisher(
            fileSystem,
            format: InputImageFormat.Png);
        var job = CreateJob();
        var temporaryOutput = publisher.CreateTemporaryFile(job);

        var exception = Assert.Throws<OutputPublicationException>(
            () => publisher.Publish(job, temporaryOutput));

        Assert.Equal(CompressionErrorCategory.OutputValidationFailed, exception.Category);
        Assert.False(fileSystem.FileExists(fileSystem.Path("temp.tmp")));
    }

    [Fact]
    public void Publish_preserves_target_when_a_race_creates_conflict()
    {
        var fileSystem = new StubOutputFileSystem("temp.tmp", "output.jpg");
        var publisher = CreatePublisher(fileSystem);
        var job = CreateJob();
        var temporaryOutput = publisher.CreateTemporaryFile(job);

        var exception = Assert.Throws<OutputPublicationException>(
            () => publisher.Publish(job, temporaryOutput));

        Assert.Equal(CompressionErrorCategory.OutputConflict, exception.Category);
        Assert.True(fileSystem.FileExists(fileSystem.Path("output.jpg")));
        Assert.False(fileSystem.FileExists(fileSystem.Path("temp.tmp")));
    }

    [Fact]
    public void Publish_rejects_temporary_output_outside_target_directory()
    {
        var fileSystem = new StubOutputFileSystem
        {
            TemporaryPath = "other/temp.tmp"
        };
        var publisher = CreatePublisher(fileSystem);
        var job = CreateJob();
        var temporaryOutput = publisher.CreateTemporaryFile(job);

        var exception = Assert.Throws<OutputPublicationException>(
            () => publisher.Publish(job, temporaryOutput));

        Assert.Equal(CompressionErrorCategory.InvalidArguments, exception.Category);
        Assert.True(fileSystem.FileExists(fileSystem.Path("other/temp.tmp")));
    }

    private static SafeOutputPublisher CreatePublisher(
        IOutputFileSystem fileSystem,
        InputImageFormat format = InputImageFormat.Jpeg,
        int width = 10,
        int height = 10,
        long fileSize = 50) =>
        new(fileSystem, new StubInspector(new InputImageInfo(format, width, height, fileSize)));

    private static CompressionJob CreateJob(
        long inputSize = 100,
        LargerOutputPolicy largerOutputPolicy = LargerOutputPolicy.Discard,
        int minimumSavingsPercent = 0) =>
        new(
            Guid.NewGuid(),
            Path.GetFullPath("input.png"),
            Path.GetFullPath("output.jpg"),
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Private,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            largerOutputPolicy,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, 10, 10, inputSize),
            minimumSavingsPercent: minimumSavingsPercent);

    private sealed class StubInspector(InputImageInfo result) : IInputImageInspector
    {
        public InputImageInfo Inspect(string path) => result;
    }

    private sealed class StubOutputFileSystem(params string[] files) : IOutputFileSystem
    {
        private readonly HashSet<string> existing = new(
            files.Select(path => System.IO.Path.GetFullPath(path)),
            StringComparer.OrdinalIgnoreCase);

        public string Path(string path) => System.IO.Path.GetFullPath(path);

        public string TemporaryPath { get; init; } = "temp.tmp";

        public string GetCanonicalPath(string path) => Path(path);

        public bool FileExists(string path) => existing.Contains(path);

        public bool PathsEqual(string left, string right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left, right);

        public string CreateTemporaryFile(string targetPath)
        {
            var path = Path(TemporaryPath);
            existing.Add(path);
            return path;
        }

        public void DeleteFile(string path) => existing.Remove(path);

        public void MoveFile(string sourcePath, string targetPath, bool overwrite)
        {
            if (!existing.Remove(sourcePath))
            {
                throw new FileNotFoundException();
            }

            if (!overwrite && existing.Contains(targetPath))
            {
                existing.Add(sourcePath);
                throw new IOException("Target exists.");
            }

            existing.Add(targetPath);
        }
    }
}
