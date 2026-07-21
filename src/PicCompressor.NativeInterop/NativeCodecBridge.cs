using System.Diagnostics;
using System.Runtime.InteropServices;
using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.NativeInterop;

public sealed class NativeCodecBridge(TimeProvider timeProvider) : INativeCodecBridge
{
    private const int ErrorCapacity = 1024;

    public EngineCapability GetEngineCapability(string engineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);

        try
        {
            if (NativeMethods.GetAbiVersion() != NativeMethods.AbiVersion)
            {
                return EngineCapability.Unavailable(
                    engineId,
                    "Native wrapper ABI version does not match the managed bridge.");
            }

            var engine = ToNativeEngine(engineId);
            return NativeMethods.IsEngineAvailable(engine) != 0
                ? EngineCapability.Available(
                    engineId,
                    ReadString(NativeMethods.GetEngineBuildVersion(engine)),
                    ReadString(NativeMethods.GetEngineSourceRevision(engine)))
                : EngineCapability.Unavailable(
                    engineId,
                    ReadString(NativeMethods.GetEngineUnavailableReason(engine)));
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            return EngineCapability.Unavailable(engineId, exception.Message);
        }
    }

    public Task<NativeCodecResult> EncodeJpegliAsync(
        string inputPath,
        string outputPath,
        JpegliSettings settings,
        RgbColor alphaBackground,
        ExifPolicy exifPolicy,
        ColorProfilePolicy colorProfilePolicy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(settings);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(
                new NativeCodecResult(
                    NativeCodecStatus.Canceled,
                    "Encoding was canceled.",
                    TimeSpan.Zero));
        }

        return Task.Run(
            () => EncodeJpegli(
                inputPath,
                outputPath,
                settings,
                alphaBackground,
                exifPolicy,
                colorProfilePolicy,
                cancellationToken),
            CancellationToken.None);
    }

    public Task<NativeCodecResult> EncodeGuetzliAsync(
        string inputPath,
        string outputPath,
        int quality,
        RgbColor alphaBackground,
        ColorProfilePolicy colorProfilePolicy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(quality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quality, 100);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(
                new NativeCodecResult(
                    NativeCodecStatus.Canceled,
                    "Encoding was canceled.",
                    TimeSpan.Zero));
        }

        return Task.Run(
            () => EncodeGuetzli(
                inputPath,
                outputPath,
                quality,
                alphaBackground,
                colorProfilePolicy,
                cancellationToken),
            CancellationToken.None);
    }

    private unsafe NativeCodecResult EncodeGuetzli(
        string inputPath,
        string outputPath,
        int quality,
        RgbColor alphaBackground,
        ColorProfilePolicy colorProfilePolicy,
        CancellationToken cancellationToken)
    {
        return Encode(
            (cancelHandle, error, errorCapacity) =>
            {
                var options = new NativeGuetzliOptions
                {
                    StructSize = (uint)sizeof(NativeGuetzliOptions),
                    Quality = quality,
                    AlphaRed = alphaBackground.Red,
                    AlphaGreen = alphaBackground.Green,
                    AlphaBlue = alphaBackground.Blue,
                    ColorProfilePolicy = ToNativeColorProfilePolicy(colorProfilePolicy)
                };
                return NativeMethods.EncodeGuetzli(
                    inputPath,
                    outputPath,
                    in options,
                    cancelHandle,
                    error,
                    errorCapacity);
            },
            cancellationToken);
    }

    private unsafe NativeCodecResult EncodeJpegli(
        string inputPath,
        string outputPath,
        JpegliSettings settings,
        RgbColor alphaBackground,
        ExifPolicy exifPolicy,
        ColorProfilePolicy colorProfilePolicy,
        CancellationToken cancellationToken)
    {
        return Encode(
            (cancelHandle, error, errorCapacity) =>
            {
                var options = new NativeJpegliOptions
                {
                    StructSize = (uint)sizeof(NativeJpegliOptions),
                    Quality = settings.Quality,
                    ChromaSubsampling = (int)settings.ChromaSubsampling,
                    ProgressiveLevel = settings.ProgressiveLevel,
                    AlphaRed = alphaBackground.Red,
                    AlphaGreen = alphaBackground.Green,
                    AlphaBlue = alphaBackground.Blue,
                    ExifPolicy = ToNativeExifPolicy(exifPolicy),
                    ColorProfilePolicy = ToNativeColorProfilePolicy(colorProfilePolicy)
                };
                return NativeMethods.EncodeJpegli(
                    inputPath,
                    outputPath,
                    in options,
                    cancelHandle,
                    error,
                    errorCapacity);
            },
            cancellationToken);
    }

    private unsafe NativeCodecResult Encode(
        NativeEncode encode,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        nint cancelHandle = 0;
        try
        {
            if (NativeMethods.GetAbiVersion() != NativeMethods.AbiVersion)
            {
                return new(
                    NativeCodecStatus.AbiMismatch,
                    "Native wrapper ABI version does not match the managed bridge.",
                    timeProvider.GetElapsedTime(startedAt));
            }

            cancelHandle = NativeMethods.CreateCancelHandle();
            if (cancelHandle == 0)
            {
                return new(
                    NativeCodecStatus.EncodeFailed,
                    "Native cancellation handle allocation failed.",
                    timeProvider.GetElapsedTime(startedAt));
            }

            using var registration = cancellationToken.Register(
                static state => NativeMethods.RequestCancel((nint)state!),
                cancelHandle);
            Span<byte> error = stackalloc byte[ErrorCapacity];
            NativeStatus status;
            fixed (byte* errorPointer = error)
            {
                status = encode(cancelHandle, errorPointer, ErrorCapacity);
            }

            return new(
                MapStatus(status),
                ReadError(error),
                timeProvider.GetElapsedTime(startedAt));
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            return new(
                NativeCodecStatus.EngineUnavailable,
                exception.Message,
                timeProvider.GetElapsedTime(startedAt));
        }
        finally
        {
            if (cancelHandle != 0)
            {
                NativeMethods.DestroyCancelHandle(cancelHandle);
            }
        }
    }

    private unsafe delegate NativeStatus NativeEncode(
        nint cancelHandle,
        byte* error,
        nuint errorCapacity);

    // Mapped explicitly rather than cast: the native ABI values are a contract
    // and must not follow a reordering of the domain enums.
    private static int ToNativeExifPolicy(ExifPolicy policy) =>
        policy switch
        {
            ExifPolicy.Keep => 0,
            ExifPolicy.Private => 1,
            ExifPolicy.Remove => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };

    private static int ToNativeColorProfilePolicy(ColorProfilePolicy policy) =>
        policy switch
        {
            ColorProfilePolicy.Preserve => 0,
            ColorProfilePolicy.Srgb => 1,
            ColorProfilePolicy.Remove => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };

    private static NativeEngine ToNativeEngine(string engineId) =>
        engineId switch
        {
            JpegliSettings.JpegliEngineId => NativeEngine.Jpegli,
            "guetzli" => NativeEngine.Guetzli,
            _ => throw new ArgumentException($"Unknown engine ID: {engineId}", nameof(engineId))
        };

    private static NativeCodecStatus MapStatus(NativeStatus status) =>
        status switch
        {
            NativeStatus.Ok => NativeCodecStatus.Succeeded,
            NativeStatus.InvalidArgument => NativeCodecStatus.InvalidArguments,
            NativeStatus.EngineUnavailable => NativeCodecStatus.EngineUnavailable,
            NativeStatus.EncodeFailed => NativeCodecStatus.EncodeFailed,
            NativeStatus.Canceled => NativeCodecStatus.Canceled,
            _ => NativeCodecStatus.EncodeFailed
        };

    private static string ReadString(nint value) =>
        Marshal.PtrToStringUTF8(value) ?? "";

    private static string? ReadError(ReadOnlySpan<byte> error)
    {
        var terminator = error.IndexOf((byte)0);
        var bytes = terminator < 0 ? error : error[..terminator];
        return bytes.IsEmpty ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }
}
