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
        var path = WriteFile("image.png", TestJpeg.Create(17, 9));

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(InputImageFormat.Jpeg, result.Format);
        Assert.Equal(17, result.Width);
        Assert.Equal(9, result.Height);
    }

    [Fact]
    public void Inspect_reports_a_plain_jpeg_as_not_yet_optimized()
    {
        var path = WriteFile("plain.jpg", TestJpeg.Create(17, 9));

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.False(result.AlreadyOptimized);
    }

    [Fact]
    public void Inspect_detects_the_optimization_marker()
    {
        // Issue #1: eine bereits von PicCompressor markierte Eingabe wird erkannt.
        var marked = JpegOptimizationMarker.Embed(TestJpeg.Create(17, 9));
        var path = WriteFile("marked.jpg", marked);

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.True(result.AlreadyOptimized);
        // Der Marker verfälscht die Bildmaße nicht.
        Assert.Equal(InputImageFormat.Jpeg, result.Format);
        Assert.Equal(17, result.Width);
        Assert.Equal(9, result.Height);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Inspect_swaps_the_axes_for_a_rotating_exif_orientation(int orientation)
    {
        // 8.2: der Encoder dreht die Pixel aufrecht, also melden die Maße das aufrechte Bild.
        var path = WriteFile($"rotated-{orientation}.jpg", TestJpeg.Create(17, 9, orientation));

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(9, result.Width);
        Assert.Equal(17, result.Height);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Inspect_keeps_the_axes_for_a_non_rotating_exif_orientation(int orientation)
    {
        var path = WriteFile($"mirrored-{orientation}.jpg", TestJpeg.Create(17, 9, orientation));

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(17, result.Width);
        Assert.Equal(9, result.Height);
    }

    [Fact]
    public void Inspect_ignores_a_malformed_exif_segment()
    {
        var jpeg = TestJpeg.Create(17, 9, 6);
        // Byte-Order-Marke im TIFF-Kopf zerstören: die Orientierung ist nicht lesbar.
        var tiffStart = Array.IndexOf(jpeg, (byte)'E') + 6;
        jpeg[tiffStart] = 0x00;
        var path = WriteFile("broken-exif.jpg", jpeg);

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(17, result.Width);
        Assert.Equal(9, result.Height);
    }

    [Fact]
    public void Inspect_reads_a_big_endian_exif_orientation()
    {
        var path = WriteFile("motorola.jpg", TestJpeg.Create(17, 9, 6, bigEndian: true));

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(9, result.Width);
        Assert.Equal(17, result.Height);
    }

    [Theory]
    [InlineData(0xffffffffu)] // würde bei 32-Bit-Addition auf einen negativen Index überlaufen
    [InlineData(0x7fffffffu)]
    [InlineData(0u)]
    [InlineData(9u)]
    public void Inspect_ignores_an_out_of_range_exif_ifd_offset(uint offset)
    {
        var jpeg = TestJpeg.Create(17, 9, 6);
        // Der IFD0-Zeiger liegt vier Byte hinter dem TIFF-Kopf (little endian).
        var pointer = Array.IndexOf(jpeg, (byte)'E') + 6 + 4;
        BitConverter.GetBytes(offset).CopyTo(jpeg, pointer);
        var path = WriteFile($"bad-offset-{offset}.jpg", jpeg);

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(17, result.Width);
        Assert.Equal(9, result.Height);
    }

    [Fact]
    public void Inspect_ignores_an_exif_entry_count_beyond_the_segment()
    {
        var jpeg = TestJpeg.Create(17, 9, 6);
        var tiff = Array.IndexOf(jpeg, (byte)'E') + 6;
        // 4096 angekündigte Einträge passen nicht in das Segment; der einzige vorhandene
        // Eintrag trägt einen anderen Tag, die Suche läuft also über das Ende hinaus.
        jpeg[tiff + 8] = 0x00;
        jpeg[tiff + 9] = 0x10;
        jpeg[tiff + 10] = 0x0f;
        jpeg[tiff + 11] = 0x01;
        var path = WriteFile("bad-count.jpg", jpeg);

        var result = new PhysicalInputImageInspector().Inspect(path);

        Assert.Equal(17, result.Width);
        Assert.Equal(9, result.Height);
    }

    [Fact]
    public void Inspect_ignores_a_truncated_exif_segment()
    {
        var jpeg = TestJpeg.Create(17, 9, 6);
        // Das APP1-Segment auf die Signatur plus zwei Byte kürzen.
        var signature = Array.IndexOf(jpeg, (byte)'E');
        var shortened = new byte[] { 0xff, 0xe1, 0x00, 0x0a };
        var path = WriteFile(
            "short-exif.jpg",
            [.. jpeg[..(signature - 4)], .. shortened, .. jpeg[signature..(signature + 8)],
             .. jpeg[(signature + 26 + 6)..]]);

        var result = new PhysicalInputImageInspector().Inspect(path);

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
        var bytes = TestJpeg.Create(17, 9);
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

    private static byte[] BigEndian(int value) =>
    [
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ];
}
