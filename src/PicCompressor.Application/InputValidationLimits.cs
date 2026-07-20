namespace PicCompressor.Application;

public sealed record InputValidationLimits
{
    public InputValidationLimits(long maxFileSizeBytes, long maxPixelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixelCount);

        MaxFileSizeBytes = maxFileSizeBytes;
        MaxPixelCount = maxPixelCount;
    }

    public long MaxFileSizeBytes { get; }
    public long MaxPixelCount { get; }
}
