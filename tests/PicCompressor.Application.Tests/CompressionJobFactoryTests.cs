using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

public sealed class CompressionJobFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_uses_canonical_input_and_safe_default_output()
    {
        var fileSystem = new StubFileSystem("photos/source.PNG");
        var factory = CreateFactory(fileSystem);

        var job = factory.Create(CreateRequest("photos/../photos/source.PNG"));

        Assert.Equal(fileSystem.Canonical("photos/source.PNG"), job.InputPath);
        Assert.Equal(fileSystem.Canonical("photos/source_compressed.jpg"), job.OutputPath);
        Assert.Equal(Now, job.CreatedAt);
        Assert.NotEqual(Guid.Empty, job.Id);
    }

    [Fact]
    public void Create_reports_missing_input()
    {
        var exception = Assert.Throws<JobCreationException>(
            () => CreateFactory(new StubFileSystem()).Create(CreateRequest("missing.png")));

        Assert.Equal(CompressionErrorCategory.InputNotFound, exception.Category);
    }

    [Fact]
    public void Create_rejects_input_as_output_without_overwrite_permission()
    {
        var fileSystem = new StubFileSystem("source.jpg");
        var request = CreateRequest("source.jpg") with { Suffix = "" };

        var exception = Assert.Throws<JobCreationException>(
            () => CreateFactory(fileSystem).Create(request));

        Assert.Equal(CompressionErrorCategory.OutputConflict, exception.Category);
    }

    [Fact]
    public void Create_allows_input_as_output_with_overwrite_permission()
    {
        var fileSystem = new StubFileSystem("source.jpg");
        var request = CreateRequest("source.jpg") with
        {
            Suffix = "",
            CollisionPolicy = CollisionPolicy.Overwrite
        };

        var job = CreateFactory(fileSystem).Create(request);

        Assert.Equal(job.InputPath, job.OutputPath);
    }

    [Fact]
    public void Create_renames_existing_output()
    {
        var fileSystem = new StubFileSystem("source.png", "source_compressed.jpg");
        var request = CreateRequest("source.png") with
        {
            CollisionPolicy = CollisionPolicy.Rename
        };

        var job = CreateFactory(fileSystem).Create(request);

        Assert.Equal(fileSystem.Canonical("source_compressed_1.jpg"), job.OutputPath);
    }

    [Fact]
    public void Create_rejects_directory_separator_in_suffix()
    {
        var fileSystem = new StubFileSystem("source.png");
        var request = CreateRequest("source.png") with
        {
            Suffix = $"{Path.DirectorySeparatorChar}nested"
        };

        var exception = Assert.Throws<JobCreationException>(
            () => CreateFactory(fileSystem).Create(request));

        Assert.Equal(CompressionErrorCategory.InvalidArguments, exception.Category);
    }

    [Fact]
    public void Create_rejects_inputs_above_pixel_limit()
    {
        var fileSystem = new StubFileSystem("source.png");
        var inspector = new StubInspector(new InputImageInfo(InputImageFormat.Png, 11, 10, 100));
        var factory = CreateFactory(fileSystem, inspector, new InputValidationLimits(100, 100));

        var exception = Assert.Throws<JobCreationException>(
            () => factory.Create(CreateRequest("source.png")));

        Assert.Equal(CompressionErrorCategory.LimitExceeded, exception.Category);
    }

    [Fact]
    public void Create_maps_invalid_image_data_to_unsupported_input()
    {
        var fileSystem = new StubFileSystem("source.png");
        var factory = CreateFactory(
            fileSystem,
            new ThrowingInspector(new InvalidDataException("broken")));

        var exception = Assert.Throws<JobCreationException>(
            () => factory.Create(CreateRequest("source.png")));

        Assert.Equal(CompressionErrorCategory.UnsupportedInput, exception.Category);
    }

    private static CompressionJobRequest CreateRequest(string inputPath) =>
        new(
            inputPath,
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Private,
            ColorProfilePolicy.Preserve,
            RgbColor.White);

    private static CompressionJobFactory CreateFactory(
        IFileSystem fileSystem,
        IInputImageInspector? inspector = null,
        InputValidationLimits? limits = null) =>
        new(
            fileSystem,
            inspector ?? new StubInspector(new InputImageInfo(InputImageFormat.Png, 1, 1, 1)),
            limits ?? new InputValidationLimits(long.MaxValue, long.MaxValue),
            new FrozenTimeProvider(Now));

    private sealed class StubFileSystem(params string[] existingFiles) : IFileSystem
    {
        private readonly HashSet<string> existing = new(
            existingFiles.Select(path => Path.GetFullPath(path)),
            StringComparer.OrdinalIgnoreCase);

        public string Canonical(string path) => Path.GetFullPath(path);

        public string GetCanonicalPath(string path) => Canonical(path);

        public bool FileExists(string path) => existing.Contains(path);

        public bool PathsEqual(string left, string right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left, right);
    }

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubInspector(InputImageInfo result) : IInputImageInspector
    {
        public InputImageInfo Inspect(string path) => result;
    }

    private sealed class ThrowingInspector(Exception exception) : IInputImageInspector
    {
        public InputImageInfo Inspect(string path) => throw exception;
    }
}
