using System.Runtime.InteropServices;

namespace PicCompressor.NativeInterop;

internal enum NativeStatus
{
    Ok = 0,
    InvalidArgument = 1,
    EngineUnavailable = 2,
    EncodeFailed = 3,
    Canceled = 4
}

internal enum NativeEngine
{
    Jpegli = 1,
    Guetzli = 2
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeJpegliOptions
{
    internal uint StructSize;
    internal int Quality;
    internal int ChromaSubsampling;
    internal int ProgressiveLevel;
    internal int AlphaRed;
    internal int AlphaGreen;
    internal int AlphaBlue;
    internal int ExifPolicy;
    internal int ColorProfilePolicy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeGuetzliOptions
{
    internal uint StructSize;
    internal int Quality;
    internal int AlphaRed;
    internal int AlphaGreen;
    internal int AlphaBlue;
    internal int ColorProfilePolicy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePreviewOptions
{
    internal uint StructSize;
    internal int MaxEdge;
    internal int AlphaRed;
    internal int AlphaGreen;
    internal int AlphaBlue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePreview
{
    internal uint StructSize;
    internal int Width;
    internal int Height;
    internal nint Rgb;
    internal nuint RgbSize;
    internal int SourceWidth;
    internal int SourceHeight;
}

internal static partial class NativeMethods
{
    internal const string LibraryName = "piccompressor_native";
    internal const uint AbiVersion = 7;

    [LibraryImport(LibraryName, EntryPoint = "pc_abi_version")]
    internal static partial uint GetAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "pc_engine_available")]
    internal static partial int IsEngineAvailable(NativeEngine engine);

    [LibraryImport(LibraryName, EntryPoint = "pc_engine_build_version")]
    internal static partial nint GetEngineBuildVersion(NativeEngine engine);

    [LibraryImport(LibraryName, EntryPoint = "pc_engine_source_revision")]
    internal static partial nint GetEngineSourceRevision(NativeEngine engine);

    [LibraryImport(LibraryName, EntryPoint = "pc_engine_unavailable_reason")]
    internal static partial nint GetEngineUnavailableReason(NativeEngine engine);

    [LibraryImport(LibraryName, EntryPoint = "pc_cancel_create")]
    internal static partial nint CreateCancelHandle();

    [LibraryImport(LibraryName, EntryPoint = "pc_cancel_request")]
    internal static partial void RequestCancel(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "pc_cancel_destroy")]
    internal static partial void DestroyCancelHandle(nint handle);

    [LibraryImport(
        LibraryName,
        EntryPoint = "pc_encode_jpegli",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial NativeStatus EncodeJpegli(
        string inputPath,
        string outputPath,
        in NativeJpegliOptions options,
        nint cancelHandle,
        byte* error,
        nuint errorCapacity);

    [LibraryImport(
        LibraryName,
        EntryPoint = "pc_render_preview",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial NativeStatus RenderPreview(
        string inputPath,
        in NativePreviewOptions options,
        ref NativePreview preview,
        nint cancelHandle,
        byte* error,
        nuint errorCapacity);

    [LibraryImport(
        LibraryName,
        EntryPoint = "pc_render_encoded_preview",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial NativeStatus RenderEncodedPreview(
        string inputPath,
        in NativeJpegliOptions options,
        in NativePreviewOptions previewOptions,
        ref NativePreview preview,
        out long encodedSize,
        nint cancelHandle,
        byte* error,
        nuint errorCapacity);

    [LibraryImport(LibraryName, EntryPoint = "pc_preview_release")]
    internal static partial void ReleasePreview(ref NativePreview preview);

    [LibraryImport(
        LibraryName,
        EntryPoint = "pc_encode_guetzli",
        StringMarshalling = StringMarshalling.Utf8)]
    internal static unsafe partial NativeStatus EncodeGuetzli(
        string inputPath,
        string outputPath,
        in NativeGuetzliOptions options,
        nint cancelHandle,
        byte* error,
        nuint errorCapacity);
}
