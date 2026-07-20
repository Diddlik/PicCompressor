using PicCompressor.Domain;

namespace PicCompressor.Domain.Tests;

public sealed class JpegliSettingsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(101, 0)]
    [InlineData(80, -1)]
    [InlineData(80, 3)]
    public void Constructor_rejects_out_of_range_values(int quality, int progressiveLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new JpegliSettings(
                quality,
                JpegliChromaSubsampling.Subsampling420,
                progressiveLevel));
    }
}
