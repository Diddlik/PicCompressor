using PicCompressor.Domain;

namespace PicCompressor.Domain.Tests;

public sealed class InputImageInfoTests
{
    [Fact]
    public void PixelCount_uses_64_bit_arithmetic()
    {
        var info = new InputImageInfo(InputImageFormat.Png, 100_000, 100_000, 1);

        Assert.Equal(10_000_000_000, info.PixelCount);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    public void Constructor_rejects_non_positive_values(int width, int height, long fileSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new InputImageInfo(InputImageFormat.Jpeg, width, height, fileSize));
    }
}
