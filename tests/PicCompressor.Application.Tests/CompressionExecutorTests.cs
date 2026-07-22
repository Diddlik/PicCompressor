using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

public sealed class CompressionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_publishes_valid_encoded_output()
    {
        var fileSystem = new StubFileSystem();
        var executor = CreateExecutor(fileSystem, EngineEncodingResult.Succeeded(TimeSpan.Zero));

        var result = await executor.ExecuteAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(JobStatus.Succeeded, result.Status);
        Assert.True(result.OutputValidated);
        Assert.True(result.OutputPublished);
        Assert.Equal(50, result.EncodedSizeBytes);
        Assert.Equal(50, result.SavedBytes);
        Assert.True(fileSystem.FileExists(Path.GetFullPath("output.jpg")));
        Assert.False(fileSystem.FileExists(fileSystem.TemporaryPath));
    }

    [Fact]
    public async Task ExecuteAsync_discards_not_smaller_output_with_warning()
    {
        var fileSystem = new StubFileSystem(outputSize: 100);
        var executor = CreateExecutor(fileSystem, EngineEncodingResult.Succeeded(TimeSpan.Zero));

        var result = await executor.ExecuteAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(JobStatus.Succeeded, result.Status);
        Assert.True(result.OutputValidated);
        Assert.False(result.OutputPublished);
        Assert.NotNull(result.Warning);
        Assert.False(fileSystem.FileExists(fileSystem.TemporaryPath));
    }

    [Fact]
    public async Task ExecuteAsync_removes_temporary_output_after_engine_failure()
    {
        var fileSystem = new StubFileSystem();
        var executor = CreateExecutor(
            fileSystem,
            EngineEncodingResult.Failed(
                CompressionErrorCategory.EngineFailed,
                "Encoder failed.",
                TimeSpan.Zero));

        var result = await executor.ExecuteAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.Equal(CompressionErrorCategory.EngineFailed, result.ErrorCategory);
        Assert.False(fileSystem.FileExists(fileSystem.TemporaryPath));
        Assert.False(fileSystem.FileExists(Path.GetFullPath("output.jpg")));
    }

    [Fact]
    public async Task ExecuteAsync_returns_canceled_and_removes_temporary_output()
    {
        var fileSystem = new StubFileSystem();
        var executor = CreateExecutor(fileSystem, EngineEncodingResult.Canceled(TimeSpan.Zero));

        var result = await executor.ExecuteAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(JobStatus.Canceled, result.Status);
        Assert.Equal(CompressionErrorCategory.Canceled, result.ErrorCategory);
        Assert.False(fileSystem.FileExists(fileSystem.TemporaryPath));
    }

    [Fact]
    public async Task ExecuteAsync_routes_to_the_engine_matching_the_job_settings()
    {
        var fileSystem = new StubFileSystem();
        var jpegli = new StubEngine(EngineEncodingResult.Succeeded(TimeSpan.Zero));
        var guetzli = new StubEngine(
            EngineEncodingResult.Succeeded(TimeSpan.Zero),
            GuetzliSettings.GuetzliEngineId);
        var executor = new CompressionExecutor(
            [jpegli, guetzli],
            new SafeOutputPublisher(fileSystem, fileSystem));

        var result = await executor.ExecuteAsync(CreateGuetzliJob(), CancellationToken.None);

        Assert.Equal(JobStatus.Succeeded, result.Status);
        Assert.Equal(GuetzliSettings.GuetzliEngineId, result.EngineId);
        Assert.True(guetzli.WasInvoked);
        Assert.False(jpegli.WasInvoked);
    }

    [Fact]
    public async Task ExecuteAsync_fails_when_the_requested_engine_is_not_configured()
    {
        var fileSystem = new StubFileSystem();
        var executor = new CompressionExecutor(
            new StubEngine(EngineEncodingResult.Succeeded(TimeSpan.Zero)),
            new SafeOutputPublisher(fileSystem, fileSystem));

        var result = await executor.ExecuteAsync(CreateGuetzliJob(), CancellationToken.None);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.Equal(CompressionErrorCategory.EngineUnavailable, result.ErrorCategory);
        Assert.Equal(GuetzliSettings.GuetzliEngineId, result.EngineId);
    }

    [Fact]
    public async Task ExecuteAsync_treats_a_runtime_timeout_as_a_limit_and_removes_the_temporary_output()
    {
        var fileSystem = new StubFileSystem();
        var executor = new CompressionExecutor(
            new HangingEngine(),
            new SafeOutputPublisher(fileSystem, fileSystem),
            timeProvider: null,
            runtimeLimits: RuntimeLimit(TimeSpan.FromMilliseconds(50)));

        var result = await executor.ExecuteAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.Equal(CompressionErrorCategory.LimitExceeded, result.ErrorCategory);
        Assert.False(fileSystem.FileExists(fileSystem.TemporaryPath));
        Assert.False(fileSystem.FileExists(Path.GetFullPath("output.jpg")));
    }

    [Fact]
    public async Task ExecuteAsync_reports_user_cancellation_even_when_a_runtime_limit_is_set()
    {
        var fileSystem = new StubFileSystem();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var executor = new CompressionExecutor(
            new HangingEngine(),
            new SafeOutputPublisher(fileSystem, fileSystem),
            timeProvider: null,
            runtimeLimits: RuntimeLimit(TimeSpan.FromHours(1)));

        var result = await executor.ExecuteAsync(CreateJob(), cancellation.Token);

        Assert.Equal(JobStatus.Canceled, result.Status);
        Assert.Equal(CompressionErrorCategory.Canceled, result.ErrorCategory);
        Assert.False(fileSystem.FileExists(fileSystem.TemporaryPath));
    }

    [Fact]
    public async Task ExecuteAsync_publishes_when_encoding_finishes_within_the_runtime_limit()
    {
        var fileSystem = new StubFileSystem();
        var executor = new CompressionExecutor(
            new StubEngine(EngineEncodingResult.Succeeded(TimeSpan.Zero)),
            new SafeOutputPublisher(fileSystem, fileSystem),
            timeProvider: null,
            runtimeLimits: RuntimeLimit(TimeSpan.FromHours(1)));

        var result = await executor.ExecuteAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(JobStatus.Succeeded, result.Status);
        Assert.True(result.OutputPublished);
    }

    private static EngineRuntimeLimits RuntimeLimit(TimeSpan limit) =>
        new(new Dictionary<string, TimeSpan> { [JpegliSettings.JpegliEngineId] = limit });

    private static CompressionExecutor CreateExecutor(
        StubFileSystem fileSystem,
        EngineEncodingResult encodingResult) =>
        new(
            new StubEngine(encodingResult),
            new SafeOutputPublisher(fileSystem, fileSystem));

    private static CompressionJob CreateGuetzliJob() =>
        new(
            Guid.NewGuid(),
            Path.GetFullPath("input.png"),
            Path.GetFullPath("output.jpg"),
            new GuetzliSettings(90),
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Discard,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, 10, 10, 100));

    private static CompressionJob CreateJob() =>
        new(
            Guid.NewGuid(),
            Path.GetFullPath("input.png"),
            Path.GetFullPath("output.jpg"),
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Private,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Discard,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, 10, 10, 100));

    private sealed class StubEngine(
        EngineEncodingResult result,
        string engineId = JpegliSettings.JpegliEngineId) : ICompressionEngine
    {
        public string EngineId => engineId;

        public bool WasInvoked { get; private set; }

        public Task<EngineCapability> DetectCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EngineCapability.Available(EngineId, "test", "test"));

        public Task<EngineEncodingResult> EncodeAsync(
            CompressionJob job,
            string temporaryOutputPath,
            CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return Task.FromResult(result);
        }
    }

    /// <summary>Simuliert einen hängenden Encoder: läuft, bis das Abbruch-Handle greift.</summary>
    private sealed class HangingEngine : ICompressionEngine
    {
        public string EngineId => JpegliSettings.JpegliEngineId;

        public Task<EngineCapability> DetectCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(EngineCapability.Available(EngineId, "test", "test"));

        public async Task<EngineEncodingResult> EncodeAsync(
            CompressionJob job,
            string temporaryOutputPath,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return EngineEncodingResult.Succeeded(TimeSpan.Zero);
        }
    }

    private sealed class StubFileSystem(long outputSize = 50)
        : IOutputFileSystem, IInputImageInspector
    {
        private readonly HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);

        public string TemporaryPath { get; } = Path.GetFullPath("output.tmp");

        public string GetCanonicalPath(string path) => Path.GetFullPath(path);

        public bool FileExists(string path) => files.Contains(path);

        public bool PathsEqual(string left, string right) =>
            StringComparer.OrdinalIgnoreCase.Equals(left, right);

        public string CreateTemporaryFile(string targetPath)
        {
            files.Add(TemporaryPath);
            return TemporaryPath;
        }

        public void DeleteFile(string path) => files.Remove(path);

        public void MoveFile(string sourcePath, string targetPath, bool overwrite)
        {
            files.Remove(sourcePath);
            files.Add(targetPath);
        }

        public InputImageInfo Inspect(string path) =>
            new(InputImageFormat.Jpeg, 10, 10, outputSize);
    }
}
