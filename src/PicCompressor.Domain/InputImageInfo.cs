namespace PicCompressor.Domain;

public enum InputImageFormat
{
    Jpeg,
    Png
}

public sealed class InputImageInfo
{
    public InputImageInfo(
        InputImageFormat format,
        int width,
        int height,
        long fileSizeBytes,
        bool alreadyOptimized = false)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (fileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
        }

        Format = format;
        Width = width;
        Height = height;
        FileSizeBytes = fileSizeBytes;
        AlreadyOptimized = alreadyOptimized;
    }

    public InputImageFormat Format { get; }
    public int Width { get; }
    public int Height { get; }
    public long FileSizeBytes { get; }
    public long PixelCount => (long)Width * Height;

    /// <summary>Trägt die Eingabe bereits den PicCompressor-Marker (Issue #1)?</summary>
    public bool AlreadyOptimized { get; }
}
