using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Engine.Guetzli;

/// <summary>
/// Engine-Adapter der optionalen Legacy-Engine Guetzli (Abschnitt 5.2, 5.3). Übersetzt einen
/// Job in den nativen Wrapper-Aufruf. Solange die Bibliothek nicht in den Wrapper eingebunden
/// ist, meldet die Capability-Probe die Engine als nicht verfügbar; ein stiller Engine-Wechsel
/// findet nicht statt (Abschnitt 4.2).
/// </summary>
public sealed class GuetzliEngineAdapter(INativeCodecBridge nativeBridge)
    : ICompressionEngine
{
    public string EngineId => GuetzliSettings.GuetzliEngineId;

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

        if (job.EngineSettings is not GuetzliSettings settings)
        {
            return EngineEncodingResult.Failed(
                CompressionErrorCategory.InvalidArguments,
                "Job does not contain Guetzli settings.",
                TimeSpan.Zero);
        }

        var result = await nativeBridge.EncodeGuetzliAsync(
            job.InputPath,
            temporaryOutputPath,
            settings.Quality,
            job.AlphaBackground,
            job.ColorProfilePolicy,
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
                    result.ErrorText ?? "Guetzli native wrapper is unavailable.",
                    result.Duration),
            NativeCodecStatus.InvalidArguments =>
                EngineEncodingResult.Failed(
                    CompressionErrorCategory.InvalidArguments,
                    result.ErrorText ?? "Invalid Guetzli request.",
                    result.Duration),
            _ => EngineEncodingResult.Failed(
                CompressionErrorCategory.EngineFailed,
                result.ErrorText ?? "Guetzli encoding failed.",
                result.Duration)
        };
    }
}
