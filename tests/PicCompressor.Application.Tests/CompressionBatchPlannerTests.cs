using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

public sealed class CompressionBatchPlannerTests
{
    [Theory]
    [InlineData(InputImageFormat.Jpeg, true)]
    [InlineData(InputImageFormat.Png, false)]
    public void Original_replacement_reaches_the_shared_job_validation(InputImageFormat format, bool accepted)
    {
        var input = Path.GetFullPath("original.jpg");
        var factory = new CompressionJobFactory(new StubFileSystem(input), new StubInspector("", format),
            new InputValidationLimits(1000, 1000), TimeProvider.System);
        var settings = new CompressionBatchSettings(
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Remove, ColorProfilePolicy.Preserve, RgbColor.White,
            CollisionPolicy.Overwrite, LargerOutputPolicy.Discard, null, "_compressed", OverwriteOriginal: true);
        var plan = Assert.Single(new CompressionBatchPlanner(factory).Plan([new(input, "")], settings));
        if (accepted)
        {
            Assert.Equal(input, plan.Job!.OutputPath);
        }
        else
        {
            Assert.Null(plan.Job);
            Assert.Equal(CompressionErrorCategory.InvalidArguments, plan.ErrorCategory);
        }
    }

    [Fact]
    public void Plan_keeps_valid_jobs_when_another_input_is_invalid()
    {
        var valid = Path.GetFullPath("valid.png");
        var invalid = Path.GetFullPath("invalid.png");
        var output = Path.GetFullPath("output");
        var fileSystem = new StubFileSystem(valid, invalid);
        var factory = new CompressionJobFactory(
            fileSystem,
            new StubInspector(invalid),
            new InputValidationLimits(1_000, 1_000),
            TimeProvider.System);
        var planner = new CompressionBatchPlanner(factory);

        var plans = planner.Plan(
            [new(valid, "nested"), new(invalid, "")],
            new CompressionBatchSettings(
                new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
                ExifPolicy.Remove,
                ColorProfilePolicy.Preserve,
                RgbColor.White,
                CollisionPolicy.Skip,
                LargerOutputPolicy.Discard,
                output,
                "_compressed"));

        Assert.Equal(Path.Combine(output, "nested", "valid_compressed.jpg"), plans[0].Job!.OutputPath);
        Assert.Equal(CompressionErrorCategory.UnsupportedInput, plans[1].ErrorCategory);
    }

    private sealed class StubFileSystem(params string[] files) : IFileSystem
    {
        public string GetCanonicalPath(string path) => Path.GetFullPath(path);
        public bool FileExists(string path) => files.Contains(path);
        public bool PathsEqual(string left, string right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left, right);
    }

    private sealed class StubInspector(string invalidPath, InputImageFormat format = InputImageFormat.Png) : IInputImageInspector
    {
        public InputImageInfo Inspect(string path) =>
            path == invalidPath
                ? throw new InvalidDataException()
                : new(format, 10, 10, 100);
    }
}
