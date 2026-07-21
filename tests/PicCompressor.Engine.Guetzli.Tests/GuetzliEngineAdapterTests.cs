using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Engine.Guetzli.Tests;

public sealed class GuetzliEngineAdapterTests
{
    [Fact]
    public async Task DetectCapabilityAsync_uses_native_bridge_metadata()
    {
        var bridge = new StubBridge
        {
            Capability = EngineCapability.Available("guetzli", "1.0.1", "abc")
        };
        var adapter = new GuetzliEngineAdapter(bridge);

        var capability = await adapter.DetectCapabilityAsync(CancellationToken.None);

        Assert.True(capability.IsAvailable);
        Assert.Equal("1.0.1", capability.BuildVersion);
        Assert.Equal("guetzli", bridge.RequestedEngineId);
    }

    [Fact]
    public async Task EncodeAsync_passes_quality_to_native_bridge()
    {
        var bridge = new StubBridge();
        var adapter = new GuetzliEngineAdapter(bridge);
        var job = CreateJob(new GuetzliSettings(90));
        var temporaryPath = Path.GetFullPath("temporary.jpg");

        var result = await adapter.EncodeAsync(job, temporaryPath, CancellationToken.None);

        Assert.Equal(EngineEncodingStatus.Succeeded, result.Status);
        Assert.Equal(job.InputPath, bridge.InputPath);
        Assert.Equal(temporaryPath, bridge.OutputPath);
        Assert.Equal(90, bridge.Quality);
    }

    [Fact]
    public async Task EncodeAsync_rejects_a_job_without_guetzli_settings()
    {
        var bridge = new StubBridge();
        var adapter = new GuetzliEngineAdapter(bridge);
        var job = CreateJob(new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2));

        var result = await adapter.EncodeAsync(
            job,
            Path.GetFullPath("temporary.jpg"),
            CancellationToken.None);

        Assert.Equal(EngineEncodingStatus.Failed, result.Status);
        Assert.Equal(CompressionErrorCategory.InvalidArguments, result.ErrorCategory);
        Assert.Null(bridge.Quality);
    }

    [Theory]
    [InlineData(NativeCodecStatus.EngineUnavailable, CompressionErrorCategory.EngineUnavailable)]
    [InlineData(NativeCodecStatus.AbiMismatch, CompressionErrorCategory.EngineUnavailable)]
    [InlineData(NativeCodecStatus.EncodeFailed, CompressionErrorCategory.EngineFailed)]
    [InlineData(NativeCodecStatus.InvalidArguments, CompressionErrorCategory.InvalidArguments)]
    [InlineData(NativeCodecStatus.Canceled, null)]
    public async Task EncodeAsync_maps_native_status(
        NativeCodecStatus status,
        CompressionErrorCategory? category)
    {
        var bridge = new StubBridge
        {
            Result = new(status, "native error", TimeSpan.FromSeconds(1))
        };
        var adapter = new GuetzliEngineAdapter(bridge);

        var result = await adapter.EncodeAsync(
            CreateJob(new GuetzliSettings(84)),
            Path.GetFullPath("temporary.jpg"),
            CancellationToken.None);

        if (status is NativeCodecStatus.Canceled)
        {
            Assert.Equal(EngineEncodingStatus.Canceled, result.Status);
            return;
        }

        Assert.Equal(EngineEncodingStatus.Failed, result.Status);
        Assert.Equal(category, result.ErrorCategory);
    }

    private static CompressionJob CreateJob(CompressionEngineSettings settings) =>
        new(
            Guid.NewGuid(),
            Path.GetFullPath("input.png"),
            Path.GetFullPath("output.jpg"),
            settings,
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Discard,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, 10, 10, 100));

    private sealed class StubBridge : INativeCodecBridge
    {
        public EngineCapability Capability { get; init; } =
            EngineCapability.Available("guetzli", "test", "test");

        public NativeCodecResult Result { get; init; } =
            new(NativeCodecStatus.Succeeded, null, TimeSpan.FromMilliseconds(10));

        public string? RequestedEngineId { get; private set; }
        public string? InputPath { get; private set; }
        public string? OutputPath { get; private set; }
        public int? Quality { get; private set; }

        public EngineCapability GetEngineCapability(string engineId)
        {
            RequestedEngineId = engineId;
            return Capability;
        }

        public Task<NativeCodecResult> EncodeJpegliAsync(
            string inputPath,
            string outputPath,
            JpegliSettings settings,
            RgbColor alphaBackground,
            ExifPolicy exifPolicy,
            ColorProfilePolicy colorProfilePolicy,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NativeCodecResult> EncodeGuetzliAsync(
            string inputPath,
            string outputPath,
            int quality,
            CancellationToken cancellationToken)
        {
            InputPath = inputPath;
            OutputPath = outputPath;
            Quality = quality;
            return Task.FromResult(Result);
        }
    }
}
