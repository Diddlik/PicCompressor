using PicCompressor.Domain;

namespace PicCompressor.Application.Tests;

public sealed class EngineCatalogTests
{
    [Fact]
    public async Task DetectCapabilitiesAsync_keeps_other_engines_available()
    {
        var catalog = new EngineCatalog(
        [
            new StubEngine(
                "jpegli",
                EngineCapability.Available("jpegli", "1", "abc")),
            new StubEngine("guetzli", new IOException("probe failed"))
        ]);

        var capabilities = await catalog.DetectCapabilitiesAsync(CancellationToken.None);

        Assert.Collection(
            capabilities,
            capability => Assert.True(capability.IsAvailable),
            capability =>
            {
                Assert.False(capability.IsAvailable);
                Assert.Equal("probe failed", capability.UnavailableReason);
            });
    }

    [Fact]
    public void Constructor_rejects_duplicate_engine_ids()
    {
        Assert.Throws<ArgumentException>(
            () => new EngineCatalog(
            [
                new StubEngine("jpegli", EngineCapability.Unavailable("jpegli", "a")),
                new StubEngine("jpegli", EngineCapability.Unavailable("jpegli", "b"))
            ]));
    }

    private sealed class StubEngine : ICompressionEngine
    {
        private readonly EngineCapability? capability;
        private readonly Exception? exception;

        public StubEngine(string engineId, EngineCapability capability)
        {
            EngineId = engineId;
            this.capability = capability;
        }

        public StubEngine(string engineId, Exception exception)
        {
            EngineId = engineId;
            this.exception = exception;
        }

        public string EngineId { get; }

        public Task<EngineCapability> DetectCapabilityAsync(
            CancellationToken cancellationToken) =>
            exception is null
                ? Task.FromResult(capability!)
                : Task.FromException<EngineCapability>(exception);

        public Task<EngineEncodingResult> EncodeAsync(
            CompressionJob job,
            string temporaryOutputPath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
