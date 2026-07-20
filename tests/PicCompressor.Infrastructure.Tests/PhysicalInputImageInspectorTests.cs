namespace PicCompressor.Infrastructure.Tests;

public sealed class PhysicalInputImageInspectorTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"PicCompressor-{Guid.NewGuid():N}");

    public PhysicalInputImageInspectorTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public void Inspect_detects_png_by_content()
    {
        var path = WriteFile("image.jpg", CreatePng(13, 7));

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(InputImageFormat.Png, result.Format);
        Assert.Equal(13, result.Width);
        Assert.Equal(7, result.Height);
    }

    [Fact]
    public void Inspect_detects_jpeg_by_content()
    {
        var path = WriteFile("image.png", CreateJpeg(17, 9));

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(InputImageFormat.Jpeg, result.Format);
        Assert.Equal(17, result.Width);
        Assert.Equal(9, result.Height);
    }

    [Fact]
    public void Inspect_rejects_unknown_content()
    {
        var path = WriteFile("image.png", [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Throws<InvalidDataException>(
            () => new PhysicalInputImageInspector().Inspect(path));
    }

    [Fact]
    public void Inspect_rejects_truncated_jpeg()
    {
        var bytes = CreateJpeg(17, 9);
        var path = WriteFile("image.jpg", bytes[..^2]);

        Assert.Throws<InvalidDataException>(
            () => new PhysicalInputImageInspector().Inspect(path));
    }

    public void Dispose()
    {
        Directory.Delete(directory, true);
        GC.SuppressFinalize(this);
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] CreatePng(int width, int height) =>
    [
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
        0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52,
        .. BigEndian(width),
        .. BigEndian(height),
        0x08, 0x02, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x44, 0x41, 0x54,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44,
        0x00, 0x00, 0x00, 0x00
    ];

    private static byte[] CreateJpeg(int width, int height) =>
    [
        0xff, 0xd8,
        0xff, 0xc0, 0x00, 0x0b, 0x08,
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0x01, 0x01, 0x11, 0x00,
        0xff, 0xda, 0x00, 0x08,
        0x01, 0x01, 0x00, 0x00, 0x3f, 0x00,
        0x11, 0x22,
        0xff, 0xd9
    ];

    private static byte[] BigEndian(int value) =>
    [
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ];
}
