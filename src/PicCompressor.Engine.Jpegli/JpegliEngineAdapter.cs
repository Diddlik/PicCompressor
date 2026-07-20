using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Engine.Jpegli;

public sealed class JpegliEngineAdapter(INativeCodecBridge nativeBridge)
    : ICompressionEngine
{
    public string EngineId => JpegliSettings.JpegliEngineId;

    public Task<EngineCapability> DetectCapabilityAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(nativeBridge.GetEngineCapability(EngineId));
    }

    public async Task<EngineEncodingResult> EncodeAsync(
        CompressionJob job,
        string temporaryOutputPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryOutputPath);

        if (job.EngineSettings is not JpegliSettings settings)
        {
            return EngineEncodingResult.Failed(
                CompressionErrorCategory.InvalidArguments,
                "Job does not contain Jpegli settings.",
                TimeSpan.Zero);
        }

        if (job.ExifPolicy is not ExifPolicy.Remove
            || job.ColorProfilePolicy is not ColorProfilePolicy.Preserve)
        {
            return EngineEncodingResult.Failed(
                CompressionErrorCategory.InvalidArguments,
                "Jpegli currently supports ExifPolicy.Remove and ColorProfilePolicy.Preserve.",
                TimeSpan.Zero);
        }

        var result = await nativeBridge.EncodeJpegliAsync(
            job.InputPath,
            temporaryOutputPath,
            settings,
            job.AlphaBackground,
            cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            NativeCodecStatus.Succeeded =>
                EngineEncodingResult.Succeeded(result.Duration),
            NativeCodecStatus.Canceled =>
                EngineEncodingResult.Canceled(result.Duration),
            NativeCodecStatus.EngineUnavailable or NativeCodecStatus.AbiMismatch =>
                EngineEncodingResult.Failed(
                    CompressionErrorCategory.EngineUnavailable,
                    result.ErrorText ?? "Jpegli native wrapper is unavailable.",
                    result.Duration),
            NativeCodecStatus.InvalidArguments =>
                EngineEncodingResult.Failed(
                    CompressionErrorCategory.InvalidArguments,
                    result.ErrorText ?? "Invalid Jpegli request.",
                    result.Duration),
            _ => EngineEncodingResult.Failed(
                CompressionErrorCategory.EngineFailed,
                result.ErrorText ?? "Jpegli encoding failed.",
                result.Duration)
        };
    }
}
