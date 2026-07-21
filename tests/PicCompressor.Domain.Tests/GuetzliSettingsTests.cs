using PicCompressor.Domain;

namespace PicCompressor.Domain.Tests;

public sealed class GuetzliSettingsTests
{
    [Theory]
    [InlineData(83)]
    [InlineData(0)]
    [InlineData(101)]
    public void Constructor_rejects_quality_outside_the_supported_range(int quality)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GuetzliSettings(quality));
    }

    [Theory]
    [InlineData(84)]
    [InlineData(100)]
    public void Constructor_accepts_quality_within_the_supported_range(int quality)
    {
        var settings = new GuetzliSettings(quality);

        Assert.Equal(quality, settings.Quality);
        Assert.Equal(GuetzliSettings.GuetzliEngineId, settings.EngineId);
    }
}
