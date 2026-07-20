using System.Runtime.InteropServices;

namespace PicCompressor.NativeInterop;

internal static class NativeLibraryLoader
{
    internal static void Configure(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException("Native library path must be absolute.", nameof(absolutePath));
        }

        NativeLibrary.SetDllImportResolver(
            typeof(NativeLibraryLoader).Assembly,
            (libraryName, _, _) =>
                libraryName == NativeMethods.LibraryName
                    ? NativeLibrary.Load(absolutePath)
                    : 0);
    }
}
