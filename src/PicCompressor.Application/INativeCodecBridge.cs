using PicCompressor.Domain;

namespace PicCompressor.Application;

public enum NativeCodecStatus
{
    Succeeded,
    InvalidArguments,
    EngineUnavailable,
    EncodeFailed,
    Canceled,
    AbiMismatch
}

public sealed record NativeCodecResult(
    NativeCodecStatus Status,
    string? ErrorText,
    TimeSpan Duration);

public interface INativeCodecBridge
{
    EngineCapability GetEngineCapability(string engineId);

    Task<NativeCodecResult> EncodeJpegliAsync(
        string inputPath,
        string outputPath,
        JpegliSettings settings,
        RgbColor alphaBackground,
        ExifPolicy exifPolicy,
        ColorProfilePolicy colorProfilePolicy,
        CancellationToken cancellationToken);

    Task<NativeCodecResult> EncodeGuetzliAsync(
        string inputPath,
        string outputPath,
        int quality,
        RgbColor alphaBackground,
        ColorProfilePolicy colorProfilePolicy,
        CancellationToken cancellationToken);
}
