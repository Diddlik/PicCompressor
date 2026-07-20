using PicCompressor.Application;
using PicCompressor.Domain;
using PicCompressor.Gui.Services;

namespace PicCompressor.Desktop.Tests;

public sealed class ApplicationCompressionServiceTests
{
    [Fact]
    public async Task CompressAsync_maps_application_result_and_reports_phases()
    {
        var input = Path.GetFullPath("input.png");
        var fileSystem = new StubFileSystem(input);
        var factory = new CompressionJobFactory(
            fileSystem,
            new StubInspector(),
            new InputValidationLimits(1_000, 1_000),
            TimeProvider.System);
        var service = new ApplicationCompressionService(
            factory,
            new StubExecutor());
        var phases = new List<JobStatus>();

        var outcome = await service.CompressAsync(
            new CompressionRequest(
                input,
                new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
                ExifPolicy.Remove,
                ColorProfilePolicy.Preserve,
                RgbColor.White,
                CollisionPolicy.Skip,
                LargerOutputPolicy.Keep,
                null,
                "_compressed"),
            new InlineProgress<CompressionProgress>(progress => phases.Add(progress.Status)),
            CancellationToken.None);

        Assert.Equal(JobStatus.Succeeded, outcome.Status);
        Assert.True(outcome.OutputPublished);
        Assert.Equal(
            [JobStatus.Validating, JobStatus.WaitingForResources, JobStatus.Encoding, JobStatus.Finalizing],
            phases);
    }

    [Fact]
    public async Task Engine_catalog_maps_capabilities_without_claiming_missing_engines()
    {
        var service = new ApplicationEngineCatalogService(
            new StubCatalog(
                EngineCapability.Available(JpegliSettings.JpegliEngineId, "0.12.0", "revision"),
                EngineCapability.Unavailable("guetzli", "Not packaged.")));

        var engines = await service.GetEnginesAsync(CancellationToken.None);

        Assert.Collection(
            engines,
            engine =>
            {
                Assert.True(engine.IsAvailable);
                Assert.Equal("0.12.0", engine.Version);
            },
            engine =>
            {
                Assert.False(engine.IsAvailable);
                Assert.Equal("Not packaged.", engine.UnavailableReason);
            });
    }

    [Fact]
    public async Task CompressBatchAsync_uses_the_application_parallelism_limit()
    {
        var first = Path.GetFullPath("first.png");
        var second = Path.GetFullPath("second.png");
        var executor = new TrackingExecutor();
        var service = new ApplicationCompressionService(
            new CompressionJobFactory(
                new StubFileSystem(first, second),
                new StubInspector(),
                new InputValidationLimits(1_000, 1_000),
                TimeProvider.System),
            executor);
        var settings = new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2);

        var outcomes = await service.CompressBatchAsync(
            [CreateRequest(first, settings), CreateRequest(second, settings)],
            2,
            null,
            CancellationToken.None);

        Assert.Equal(2, executor.MaxConcurrent);
        Assert.All(outcomes, outcome => Assert.Equal(JobStatus.Succeeded, outcome.Status));
    }

    private static CompressionRequest CreateRequest(
        string inputPath,
        CompressionEngineSettings settings) =>
        new(
            inputPath,
            settings,
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Keep,
            null,
            "_compressed");

    private sealed class StubFileSystem(params string[] files) : IFileSystem
    {
        public string GetCanonicalPath(string path) => Path.GetFullPath(path);
        public bool FileExists(string path) => files.Contains(path);
        public bool PathsEqual(string left, string right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left, right);
    }

    private sealed class StubInspector : IInputImageInspector
    {
        public InputImageInfo Inspect(string path) =>
            new(InputImageFormat.Png, 10, 10, 100);
    }

    private sealed class StubExecutor : ICompressionJobExecutor
    {
        public Task<CompressionExecutionResult> ExecuteAsync(
            CompressionJob job,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(
                new CompressionExecutionResult(
                    job.Id,
                    JobStatus.Succeeded,
                    job.InputPath,
                    job.OutputPath,
                    job.EngineSettings.EngineId,
                    "test",
                    100,
                    50,
                    now,
                    now,
                    true,
                    true,
                    null,
                    null,
                    null));
        }
    }

    private sealed class TrackingExecutor : ICompressionJobExecutor
    {
        private int concurrent;

        public int MaxConcurrent { get; private set; }

        public async Task<CompressionExecutionResult> ExecuteAsync(
            CompressionJob job,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref concurrent);
            MaxConcurrent = Math.Max(MaxConcurrent, current);
            await Task.Delay(25, cancellationToken);
            Interlocked.Decrement(ref concurrent);
            var now = DateTimeOffset.UtcNow;
            return new(
                job.Id,
                JobStatus.Succeeded,
                job.InputPath,
                job.OutputPath,
                job.EngineSettings.EngineId,
                "test",
                100,
                50,
                now,
                now,
                true,
                true,
                null,
                null,
                null);
        }
    }

    private sealed class StubCatalog(params EngineCapability[] capabilities) : IEngineCatalog
    {
        public Task<IReadOnlyList<EngineCapability>> DetectCapabilitiesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EngineCapability>>(capabilities);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
