using System.Text;
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
                ExifPolicy.Remove,
                ColorProfilePolicy.Preserve,
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
                ExifPolicy.Remove,
                ColorProfilePolicy.Preserve,
                CancellationToken.None);

            Assert.Equal(NativeCodecStatus.Succeeded, secondResult.Status);
            Assert.True(File.Exists(secondOutputPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(1, 3, 2)]
    [InlineData(3, 3, 2)]
    [InlineData(6, 2, 3)]
    [InlineData(8, 2, 3)]
    public async Task EncodeJpegliAsync_applies_exif_orientation_to_pixels(
        int orientation,
        int expectedWidth,
        int expectedHeight)
    {
        using var workspace = new Workspace();

        var source = await EncodeAsync(
            workspace,
            "source",
            CreatePortablePixmap(3, 2),
            ExifPolicy.Remove);
        var oriented = workspace.Write(
            "oriented.jpg",
            InsertExif(source, CreateExifTiff(orientation, artist: null)));

        var output = await EncodeAsync(
            workspace,
            "rotated",
            await File.ReadAllBytesAsync(oriented),
            ExifPolicy.Remove);

        var (width, height) = ReadJpegSize(output);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public async Task EncodeJpegliAsync_keeps_exif_but_normalises_orientation()
    {
        using var workspace = new Workspace();

        var source = await EncodeAsync(
            workspace,
            "source",
            CreatePortablePixmap(3, 2),
            ExifPolicy.Remove);
        var input = InsertExif(source, CreateExifTiff(6, artist: null));

        var output = await EncodeAsync(workspace, "kept", input, ExifPolicy.Keep);

        var exif = FindExif(output);
        Assert.NotNull(exif);
        // The pixels were rotated, so a surviving orientation tag would rotate
        // the image a second time in any viewer.
        Assert.Equal(1, ReadOrientation(exif));
    }

    [Fact]
    public async Task EncodeJpegliAsync_removes_exif_completely()
    {
        using var workspace = new Workspace();

        var source = await EncodeAsync(
            workspace,
            "source",
            CreatePortablePixmap(3, 2),
            ExifPolicy.Remove);
        var input = InsertExif(source, CreateExifTiff(1, "Bob"));

        var output = await EncodeAsync(workspace, "removed", input, ExifPolicy.Remove);

        Assert.Null(FindExif(output));
    }

    [Fact]
    public async Task EncodeJpegliAsync_strips_identifying_exif_fields()
    {
        using var workspace = new Workspace();

        var source = await EncodeAsync(
            workspace,
            "source",
            CreatePortablePixmap(3, 2),
            ExifPolicy.Remove);
        var input = InsertExif(source, CreateExifTiff(1, "Bob"));
        Assert.Contains("Bob", Encoding.ASCII.GetString(input), StringComparison.Ordinal);

        var output = await EncodeAsync(workspace, "private", input, ExifPolicy.Private);

        Assert.NotNull(FindExif(output));
        Assert.DoesNotContain(
            "Bob",
            Encoding.ASCII.GetString(output),
            StringComparison.Ordinal);
    }

    private async Task<byte[]> EncodeAsync(
        Workspace workspace,
        string name,
        byte[] input,
        ExifPolicy exifPolicy)
    {
        var inputPath = workspace.Write($"{name}-input.bin", input);
        var outputPath = Path.Combine(workspace.Directory, $"{name}-output.jpg");

        var result = await Bridge.EncodeJpegliAsync(
            inputPath,
            outputPath,
            new JpegliSettings(90, JpegliChromaSubsampling.Subsampling444, 0),
            RgbColor.White,
            exifPolicy,
            ColorProfilePolicy.Preserve,
            CancellationToken.None);

        Assert.Equal(NativeCodecStatus.Succeeded, result.Status);
        return await File.ReadAllBytesAsync(outputPath);
    }

    private sealed class Workspace : IDisposable
    {
        public string Directory { get; } = Path.Combine(
            Path.GetTempPath(),
            $"piccompressor-{Guid.NewGuid():N}");

        public Workspace() => System.IO.Directory.CreateDirectory(Directory);

        public string Write(string name, byte[] content)
        {
            var path = Path.Combine(Directory, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }

    // A binary PPM is the smallest input format Jpegli decodes without needing
    // a hand-written PNG or JPEG fixture.
    private static byte[] CreatePortablePixmap(int width, int height)
    {
        var header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        var pixels = new byte[width * height * 3];
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index] = (byte)(index * 37 % 256);
        }
        return [.. header, .. pixels];
    }

    private static byte[] CreateExifTiff(int orientation, string? artist)
    {
        var entries = new List<(ushort Tag, ushort Type, uint Count, byte[] Value)>
        {
            (0x0112, 3, 1, [(byte)orientation, 0])
        };
        if (artist is not null)
        {
            var value = Encoding.ASCII.GetBytes(artist + "\0");
            entries.Add((0x013b, 2, (uint)value.Length, value));
        }

        var body = new List<byte> { 0x49, 0x49, 0x2a, 0x00 };
        var data = new List<byte>();
        var dataOffset = 8 + 2 + (entries.Count * 12) + 4;

        body.AddRange(BitConverter.GetBytes(8));
        body.AddRange(BitConverter.GetBytes((ushort)entries.Count));
        foreach (var entry in entries)
        {
            body.AddRange(BitConverter.GetBytes(entry.Tag));
            body.AddRange(BitConverter.GetBytes(entry.Type));
            body.AddRange(BitConverter.GetBytes(entry.Count));
            if (entry.Value.Length <= 4)
            {
                body.AddRange(entry.Value);
                body.AddRange(new byte[4 - entry.Value.Length]);
            }
            else
            {
                body.AddRange(BitConverter.GetBytes(dataOffset + data.Count));
                data.AddRange(entry.Value);
            }
        }
        body.AddRange(new byte[4]);
        body.AddRange(data);
        return [.. body];
    }

    private static byte[] InsertExif(byte[] jpeg, byte[] tiff)
    {
        var payload = new List<byte>(Encoding.ASCII.GetBytes("Exif")) { 0, 0 };
        payload.AddRange(tiff);
        var length = payload.Count + 2;

        var result = new List<byte>();
        result.AddRange(jpeg[..2]);
        result.AddRange([0xff, 0xe1, (byte)(length >> 8), (byte)(length & 0xff)]);
        result.AddRange(payload);
        result.AddRange(jpeg[2..]);
        return [.. result];
    }

    private static (int Width, int Height) ReadJpegSize(byte[] jpeg)
    {
        foreach (var (marker, offset, _) in EnumerateSegments(jpeg))
        {
            // Every start-of-frame marker except DHT, JPG and DAC.
            if (marker is >= 0xc0 and <= 0xcf and not 0xc4 and not 0xc8 and not 0xcc)
            {
                return (
                    (jpeg[offset + 7] << 8) | jpeg[offset + 8],
                    (jpeg[offset + 5] << 8) | jpeg[offset + 6]);
            }
        }
        throw new InvalidOperationException("The output carries no frame header.");
    }

    private static byte[]? FindExif(byte[] jpeg)
    {
        var signature = Encoding.ASCII.GetBytes("Exif\0\0");
        foreach (var (marker, offset, length) in EnumerateSegments(jpeg))
        {
            if (marker != 0xe1 || length < 2 + signature.Length)
            {
                continue;
            }
            var payload = jpeg.AsSpan(offset + 4, length - 2);
            if (payload[..signature.Length].SequenceEqual(signature))
            {
                return payload[signature.Length..].ToArray();
            }
        }
        return null;
    }

    private static int ReadOrientation(byte[] tiff)
    {
        var count = BitConverter.ToUInt16(tiff, 8);
        for (var index = 0; index < count; index++)
        {
            var entry = 10 + (index * 12);
            if (BitConverter.ToUInt16(tiff, entry) == 0x0112)
            {
                return BitConverter.ToUInt16(tiff, entry + 8);
            }
        }
        throw new InvalidOperationException("The blob carries no orientation.");
    }

    private static IEnumerable<(byte Marker, int Offset, int Length)>
        EnumerateSegments(byte[] jpeg)
    {
        var offset = 2;
        while (offset + 4 <= jpeg.Length && jpeg[offset] == 0xff)
        {
            var marker = jpeg[offset + 1];
            if (marker == 0xda)
            {
                yield break;
            }
            var length = (jpeg[offset + 2] << 8) | jpeg[offset + 3];
            yield return (marker, offset, length);
            offset += 2 + length;
        }
    }

    [Fact]
    public async Task RenderPreviewAsync_downscales_to_the_requested_edge()
    {
        using var workspace = new Workspace();
        var inputPath = workspace.Write("large.ppm", CreatePortablePixmap(300, 200));

        var result = await Bridge.RenderPreviewAsync(
            inputPath,
            100,
            RgbColor.White,
            CancellationToken.None);

        Assert.Null(result.ErrorText);
        var image = Assert.IsType<PreviewImage>(result.Image);
        Assert.Equal(100, image.Width);
        Assert.Equal(66, image.Height);
        Assert.Equal(image.Width * image.Height * 3, image.Rgb.Length);

        // Die Maße des Originals müssen mitkommen, sonst lässt sich der Anzeigemaßstab
        // nicht bestimmen.
        Assert.Equal(300, image.SourceWidth);
        Assert.Equal(200, image.SourceHeight);
        Assert.Equal(1.0 / 3, image.ScaleFromSource, 6);
    }

    [Fact]
    public async Task RenderPreviewAsync_reports_the_upright_source_size()
    {
        using var workspace = new Workspace();
        var source = await EncodeAsync(
            workspace,
            "upright-source",
            CreatePortablePixmap(3, 2),
            ExifPolicy.Remove);
        var inputPath = workspace.Write(
            "upright.jpg",
            InsertExif(source, CreateExifTiff(6, artist: null)));

        var result = await Bridge.RenderPreviewAsync(
            inputPath,
            512,
            RgbColor.White,
            CancellationToken.None);

        // Orientierung 6 vertauscht die Achsen; gemeldet werden die aufrechten Maße.
        var image = Assert.IsType<PreviewImage>(result.Image);
        Assert.Equal(2, image.SourceWidth);
        Assert.Equal(3, image.SourceHeight);
        Assert.Equal(1, image.ScaleFromSource);
    }

    [Fact]
    public async Task RenderPreviewAsync_keeps_small_inputs_untouched()
    {
        using var workspace = new Workspace();
        var inputPath = workspace.Write("small.ppm", CreatePortablePixmap(3, 2));

        var result = await Bridge.RenderPreviewAsync(
            inputPath,
            512,
            RgbColor.White,
            CancellationToken.None);

        var image = Assert.IsType<PreviewImage>(result.Image);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public async Task RenderPreviewAsync_applies_exif_orientation()
    {
        using var workspace = new Workspace();
        var source = await EncodeAsync(
            workspace,
            "preview-source",
            CreatePortablePixmap(3, 2),
            ExifPolicy.Remove);
        var inputPath = workspace.Write(
            "preview-oriented.jpg",
            InsertExif(source, CreateExifTiff(6, artist: null)));

        var result = await Bridge.RenderPreviewAsync(
            inputPath,
            512,
            RgbColor.White,
            CancellationToken.None);

        // Orientation 6 swaps the axes; the preview must be upright like the output.
        var image = Assert.IsType<PreviewImage>(result.Image);
        Assert.Equal(2, image.Width);
        Assert.Equal(3, image.Height);
    }

    [Fact]
    public async Task RenderEncodedPreviewAsync_compresses_without_writing_a_file()
    {
        using var workspace = new Workspace();
        var inputPath = workspace.Write("live.ppm", CreatePortablePixmap(120, 90));
        var before = Directory.GetFiles(workspace.Directory);

        var result = await Bridge.RenderEncodedPreviewAsync(
            inputPath,
            512,
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 0),
            RgbColor.White,
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            CancellationToken.None);

        Assert.Null(result.ErrorText);
        var image = Assert.IsType<PreviewImage>(result.Image);
        Assert.Equal(120, image.SourceWidth);
        Assert.Equal(90, image.SourceHeight);
        Assert.True(result.EncodedSizeBytes > 0);
        // Eine Vorschau darf nichts veröffentlichen (Abschnitt 7.2).
        Assert.Equal(before, Directory.GetFiles(workspace.Directory));
    }

    [Fact]
    public async Task RenderEncodedPreviewAsync_reports_the_size_the_quality_produces()
    {
        using var workspace = new Workspace();
        var inputPath = workspace.Write("quality.ppm", CreatePortablePixmap(160, 120));

        var low = await RenderEncodedAsync(inputPath, 30);
        var high = await RenderEncodedAsync(inputPath, 95);

        // Die gemeldete Grösse muss der Einstellung folgen, sonst wäre die Schätzung wertlos.
        Assert.True(
            low.EncodedSizeBytes < high.EncodedSizeBytes,
            $"quality 30 produced {low.EncodedSizeBytes}, quality 95 produced {high.EncodedSizeBytes}");
    }

    [Fact]
    public async Task RenderEncodedPreviewAsync_reports_an_unreadable_input()
    {
        var result = await Bridge.RenderEncodedPreviewAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"),
            512,
            new JpegliSettings(80, JpegliChromaSubsampling.Subsampling420, 0),
            RgbColor.White,
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            CancellationToken.None);

        Assert.Null(result.Image);
        Assert.Equal(0, result.EncodedSizeBytes);
        Assert.NotNull(result.ErrorText);
    }

    private static Task<EncodedPreviewResult> RenderEncodedAsync(string inputPath, int quality) =>
        Bridge.RenderEncodedPreviewAsync(
            inputPath,
            512,
            new JpegliSettings(quality, JpegliChromaSubsampling.Subsampling420, 0),
            RgbColor.White,
            ExifPolicy.Remove,
            ColorProfilePolicy.Preserve,
            CancellationToken.None);

    [Fact]
    public async Task RenderPreviewAsync_reports_an_unreadable_input()
    {
        var result = await Bridge.RenderPreviewAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"),
            512,
            RgbColor.White,
            CancellationToken.None);

        Assert.Null(result.Image);
        Assert.NotNull(result.ErrorText);
    }

    [Fact]
    public async Task RenderPreviewAsync_reports_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await Bridge.RenderPreviewAsync(
            "input.png",
            512,
            RgbColor.White,
            cancellation.Token);

        Assert.Null(result.Image);
        Assert.NotNull(result.ErrorText);
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
