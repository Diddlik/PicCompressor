using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Engine.Jpegli.Tests;

public sealed class JpegliEngineAdapterTests
{
    [Fact]
    public async Task DetectCapabilityAsync_uses_native_bridge_metadata()
    {
        var bridge = new StubBridge
        {
            Capability = EngineCapability.Available("jpegli", "1", "abc")
        };
        var adapter = new JpegliEngineAdapter(bridge);

        var capability = await adapter.DetectCapabilityAsync(CancellationToken.None);

        Assert.True(capability.IsAvailable);
        Assert.Equal("1", capability.BuildVersion);
    }

    [Fact]
    public async Task EncodeAsync_passes_typed_request_to_native_bridge()
    {
        var bridge = new StubBridge();
        var adapter = new JpegliEngineAdapter(bridge);
        var settings = new JpegliSettings(
            83,
            JpegliChromaSubsampling.Subsampling422,
            1);
        var job = CreateJob(settings);
        var temporaryPath = Path.GetFullPath("temporary.jpg");

        var result = await adapter.EncodeAsync(job, temporaryPath, CancellationToken.None);

        Assert.Equal(EngineEncodingStatus.Succeeded, result.Status);
        Assert.Equal(job.InputPath, bridge.InputPath);
        Assert.Equal(temporaryPath, bridge.OutputPath);
        Assert.Same(settings, bridge.Settings);
        Assert.Equal(RgbColor.White, bridge.AlphaBackground);
    }

    [Fact]
    public async Task EncodeAsync_rejects_unimplemented_metadata_policy()
    {
        var bridge = new StubBridge();
        var adapter = new JpegliEngineAdapter(bridge);
        var job = CreateJob(
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
            ExifPolicy.Private);

        var result = await adapter.EncodeAsync(
            job,
            Path.GetFullPath("temporary.jpg"),
            CancellationToken.None);

        Assert.Equal(EngineEncodingStatus.Failed, result.Status);
        Assert.Equal(CompressionErrorCategory.InvalidArguments, result.ErrorCategory);
        Assert.Null(bridge.Settings);
    }

    [Theory]
    [InlineData(NativeCodecStatus.EngineUnavailable, CompressionErrorCategory.EngineUnavailable)]
    [InlineData(NativeCodecStatus.EncodeFailed, CompressionErrorCategory.EngineFailed)]
    [InlineData(NativeCodecStatus.InvalidArguments, CompressionErrorCategory.InvalidArguments)]
    public async Task EncodeAsync_maps_native_status(
        NativeCodecStatus status,
        CompressionErrorCategory category)
    {
        var bridge = new StubBridge
        {
            Result = new(status, "native error", TimeSpan.FromSeconds(1))
        };
        var adapter = new JpegliEngineAdapter(bridge);

        var result = await adapter.EncodeAsync(
            CreateJob(new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2)),
            Path.GetFullPath("temporary.jpg"),
            CancellationToken.None);

        Assert.Equal(EngineEncodingStatus.Failed, result.Status);
        Assert.Equal(category, result.ErrorCategory);
        Assert.Equal("native error", result.ErrorText);
    }

    private static CompressionJob CreateJob(
        CompressionEngineSettings settings,
        ExifPolicy exifPolicy = ExifPolicy.Remove) =>
        new(
            Guid.NewGuid(),
            Path.GetFullPath("input.png"),
            Path.GetFullPath("output.jpg"),
            settings,
            exifPolicy,
            ColorProfilePolicy.Preserve,
            RgbColor.White,
            CollisionPolicy.Skip,
            LargerOutputPolicy.Discard,
            DateTimeOffset.UtcNow,
            new InputImageInfo(InputImageFormat.Png, 10, 10, 100));

    private sealed class StubBridge : INativeCodecBridge
    {
        public EngineCapability Capability { get; init; } =
            EngineCapability.Available("jpegli", "test", "test");

        public NativeCodecResult Result { get; init; } =
            new(NativeCodecStatus.Succeeded, null, TimeSpan.FromMilliseconds(10));

        public string? InputPath { get; private set; }
        public string? OutputPath { get; private set; }
        public JpegliSettings? Settings { get; private set; }
        public RgbColor AlphaBackground { get; private set; }

        public EngineCapability GetEngineCapability(string engineId) => Capability;

        public Task<NativeCodecResult> EncodeJpegliAsync(
            string inputPath,
            string outputPath,
            JpegliSettings settings,
            RgbColor alphaBackground,
            CancellationToken cancellationToken)
        {
            InputPath = inputPath;
            OutputPath = outputPath;
            Settings = settings;
            AlphaBackground = alphaBackground;
            return Task.FromResult(Result);
        }

        public Task<NativeCodecResult> EncodeGuetzliAsync(
            string inputPath,
            string outputPath,
            int quality,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
