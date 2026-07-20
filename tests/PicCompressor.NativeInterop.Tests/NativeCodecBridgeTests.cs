using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.NativeInterop.Tests;

public sealed class NativeCodecBridgeTests
{
    private static readonly NativeCodecBridge Bridge = new(TimeProvider.System);

    static NativeCodecBridgeTests()
    {
        NativeLibraryLoader.Configure(FindNativeLibrary());
    }

    [Fact]
    public void GetEngineCapability_reports_linked_jpegli()
    {
        var capability = Bridge.GetEngineCapability("jpegli");

        Assert.True(capability.IsAvailable);
        Assert.Equal("0.12.0", capability.BuildVersion);
        Assert.Equal(
            "031a0077f5799a6041004267fc12b956c1f52a20",
            capability.SourceRevision);
    }

    [Fact]
    public void GetEngineCapability_reports_unlinked_guetzli()
    {
        var capability = Bridge.GetEngineCapability("guetzli");

        Assert.False(capability.IsAvailable);
        Assert.Contains("not linked", capability.UnavailableReason);
    }

    [Fact]
    public async Task EncodeJpegliAsync_encodes_png_and_jpeg()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"piccompressor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var inputPath = Path.Combine(directory, "input.png");
            var outputPath = Path.Combine(directory, "output.jpg");
            await File.WriteAllBytesAsync(
                inputPath,
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));

            var result = await Bridge.EncodeJpegliAsync(
                inputPath,
                outputPath,
                new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 2),
                RgbColor.White,
                CancellationToken.None);

            Assert.Equal(NativeCodecStatus.Succeeded, result.Status);
            var output = await File.ReadAllBytesAsync(outputPath);
            Assert.True(output.Length > 2);
            Assert.Equal(0xff, output[0]);
            Assert.Equal(0xd8, output[1]);

            var secondOutputPath = Path.Combine(directory, "second-output.jpg");
            var secondResult = await Bridge.EncodeJpegliAsync(
                outputPath,
                secondOutputPath,
                new JpegliSettings(75, JpegliChromaSubsampling.Subsampling444, 0),
                RgbColor.White,
                CancellationToken.None);

            Assert.Equal(NativeCodecStatus.Succeeded, secondResult.Status);
            Assert.True(File.Exists(secondOutputPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task EncodeGuetzliAsync_calls_native_wrapper()
    {
        var result = await Bridge.EncodeGuetzliAsync(
            "input.png",
            "output.jpg",
            90,
            CancellationToken.None);

        Assert.Equal(NativeCodecStatus.EngineUnavailable, result.Status);
        Assert.Contains("not linked", result.ErrorText);
    }

    private static string FindNativeLibrary()
    {
        var configuredPath = Environment.GetEnvironmentVariable(
            "PICCOMPRESSOR_NATIVE_TEST_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var copiedLibrary = Path.Combine(
            AppContext.BaseDirectory,
            "piccompressor_native.dll");
        if (File.Exists(copiedLibrary))
        {
            return copiedLibrary;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var nativeDirectory = Path.Combine(directory.FullName, "native");
            var candidate = Directory.Exists(nativeDirectory)
                ? Directory
                    .EnumerateFiles(
                        nativeDirectory,
                        "piccompressor_native.dll",
                        SearchOption.AllDirectories)
                    .FirstOrDefault()
                : null;
            if (candidate is not null)
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Build piccompressor_native or set PICCOMPRESSOR_NATIVE_TEST_LIBRARY before running interop tests.");
    }
}
