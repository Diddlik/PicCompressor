namespace PicCompressor.Application.Tests;

public sealed class JpegOptimizationMarkerTests
{
    // Minimales, aber strukturell gültiges JPEG: SOI, SOF0 (1×1), SOS, EOI.
    private static readonly byte[] MinimalJpeg =
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x01, 0x00, 0x01, 0x03,
        0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00,
        0xFF, 0xD9
    ];

    [Fact]
    public void An_unmarked_jpeg_is_reported_as_unmarked()
    {
        Assert.False(JpegOptimizationMarker.IsMarked(MinimalJpeg));
    }

    [Fact]
    public void Embed_adds_a_detectable_marker_and_keeps_the_rest()
    {
        var marked = JpegOptimizationMarker.Embed(MinimalJpeg);

        Assert.True(JpegOptimizationMarker.IsMarked(marked));
        Assert.True(marked.Length > MinimalJpeg.Length);
        // SOI bleibt vorn; die Bildsegmente folgen unverändert nach dem Kommentar.
        Assert.Equal(0xFF, marked[0]);
        Assert.Equal(0xD8, marked[1]);
        Assert.Equal(0xFF, marked[2]);
        Assert.Equal(0xFE, marked[3]);
    }

    [Fact]
    public void Embedding_twice_is_idempotent()
    {
        var once = JpegOptimizationMarker.Embed(MinimalJpeg);
        var twice = JpegOptimizationMarker.Embed(once);

        Assert.Equal(once.Length, twice.Length);
        Assert.True(JpegOptimizationMarker.IsMarked(twice));
    }

    [Fact]
    public void A_non_jpeg_input_is_returned_unchanged()
    {
        byte[] notJpeg = [0x89, 0x50, 0x4E, 0x47];

        var result = JpegOptimizationMarker.Embed(notJpeg);

        Assert.Equal(notJpeg, result);
        Assert.False(JpegOptimizationMarker.IsMarked(result));
    }
}
