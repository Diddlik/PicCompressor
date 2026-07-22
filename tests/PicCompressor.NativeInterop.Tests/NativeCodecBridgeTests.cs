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
    public void GetEngineCapability_reports_linked_guetzli()
    {
        var capability = Bridge.GetEngineCapability("guetzli");

        Assert.True(capability.IsAvailable);
        Assert.Equal("1.0.1", capability.BuildVersion);
        Assert.Equal(
            "a0f47a297f802630f937a3091964838eaf3b87d8",
            capability.SourceRevision);
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
    public async Task EncodeGuetzliAsync_encodes_a_valid_jpeg()
    {
        using var workspace = new Workspace();
        var inputPath = workspace.Write("input.ppm", CreatePortablePixmap(32, 32));
        var outputPath = Path.Combine(workspace.Directory, "output.jpg");

        var result = await Bridge.EncodeGuetzliAsync(
            inputPath,
            outputPath,
            95,
            RgbColor.White,
            ColorProfilePolicy.Preserve,
            CancellationToken.None);

        Assert.Equal(NativeCodecStatus.Succeeded, result.Status);
        var output = await File.ReadAllBytesAsync(outputPath);
        Assert.True(output.Length > 2);
        Assert.Equal(0xff, output[0]);
        Assert.Equal(0xd8, output[1]);
    }

    [Fact]
    public async Task RenderPreviewAsync_flattens_transparent_pixels_onto_the_background()
    {
        using var workspace = new Workspace();
        // Left pixel: fully transparent over an arbitrary colour. Right pixel: opaque green.
        var rgba = new byte[]
        {
            10, 20, 30, 0,
            0, 255, 0, 255,
        };
        var inputPath = workspace.Write("alpha.png", CreateRgbaPng(2, 1, rgba));

        var onMagenta = await Bridge.RenderPreviewAsync(
            inputPath, 512, new RgbColor(255, 0, 255), CancellationToken.None);
        var onWhite = await Bridge.RenderPreviewAsync(
            inputPath, 512, RgbColor.White, CancellationToken.None);

        var magenta = Assert.IsType<PreviewImage>(onMagenta.Image);
        var white = Assert.IsType<PreviewImage>(onWhite.Image);

        // The transparent pixel takes the chosen background; the opaque one is untouched.
        Assert.Equal(new byte[] { 255, 0, 255 }, magenta.Rgb[..3]);
        Assert.Equal(new byte[] { 255, 255, 255 }, white.Rgb[..3]);
        Assert.Equal(new byte[] { 0, 255, 0 }, magenta.Rgb[3..6]);
        Assert.Equal(new byte[] { 0, 255, 0 }, white.Rgb[3..6]);
    }

    [Fact]
    public async Task RenderPreviewAsync_transforms_an_embedded_profile_to_srgb()
    {
        using var workspace = new Workspace();
        var gray = new byte[] { 128, 128, 128 };
        var tagged = workspace.Write(
            "tagged.png", CreateRgbPng(1, 1, gray, BuildLinearRgbIccProfile()));
        var plain = workspace.Write("plain.png", CreateRgbPng(1, 1, gray, iccProfile: null));

        var withProfile = await Bridge.RenderPreviewAsync(
            tagged, 512, RgbColor.White, CancellationToken.None);
        var withoutProfile = await Bridge.RenderPreviewAsync(
            plain, 512, RgbColor.White, CancellationToken.None);

        var transformed = Assert.IsType<PreviewImage>(withProfile.Image);
        var untouched = Assert.IsType<PreviewImage>(withoutProfile.Image);

        // The plain input is treated as sRGB and passes through unchanged.
        Assert.Equal(128, untouched.Rgb[0]);
        // A linear-encoded mid-grey re-encodes to a much brighter sRGB sample; a no-op would
        // leave it at 128.
        Assert.True(
            transformed.Rgb[0] > untouched.Rgb[0] + 30,
            $"expected the profile transform to brighten the sample, got {transformed.Rgb[0]}");
    }

    [Fact]
    public async Task EncodeJpegliAsync_rejects_removing_a_non_srgb_profile()
    {
        using var workspace = new Workspace();
        var inputPath = workspace.Write(
            "non-srgb.png", CreateRgbPng(1, 1, [128, 128, 128], BuildLinearRgbIccProfile()));
        var outputPath = Path.Combine(workspace.Directory, "out.jpg");

        var result = await Bridge.EncodeJpegliAsync(
            inputPath,
            outputPath,
            new JpegliSettings(90, JpegliChromaSubsampling.Subsampling444, 0),
            RgbColor.White,
            ExifPolicy.Remove,
            ColorProfilePolicy.Remove,
            CancellationToken.None);

        // Removing a foreign profile would silently change the colours (8.3).
        Assert.Equal(NativeCodecStatus.InvalidArguments, result.Status);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task EncodeJpegliAsync_removes_the_profile_from_an_srgb_input()
    {
        using var workspace = new Workspace();
        var inputPath = workspace.Write(
            "srgb.png", CreateRgbPng(1, 1, [128, 128, 128], iccProfile: null));
        var outputPath = Path.Combine(workspace.Directory, "out.jpg");

        var result = await Bridge.EncodeJpegliAsync(
            inputPath,
            outputPath,
            new JpegliSettings(90, JpegliChromaSubsampling.Subsampling444, 0),
            RgbColor.White,
            ExifPolicy.Remove,
            ColorProfilePolicy.Remove,
            CancellationToken.None);

        Assert.Equal(NativeCodecStatus.Succeeded, result.Status);
    }

    private static byte[] CreateRgbaPng(int width, int height, byte[] rgba) =>
        BuildPng(width, height, colorType: 6, samples: rgba, iccProfile: null);

    private static byte[] CreateRgbPng(int width, int height, byte[] rgb, byte[]? iccProfile) =>
        BuildPng(width, height, colorType: 2, samples: rgb, iccProfile);

    private static byte[] BuildPng(
        int width, int height, byte colorType, byte[] samples, byte[]? iccProfile)
    {
        var channels = colorType == 6 ? 4 : 3;
        var stride = width * channels;
        var raw = new byte[height * (1 + stride)];
        for (var y = 0; y < height; y++)
        {
            // Filter type 0 (none) prefixes each scanline.
            Array.Copy(samples, y * stride, raw, y * (1 + stride) + 1, stride);
        }

        var png = new List<byte>(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

        var ihdr = new List<byte>();
        ihdr.AddRange(BigEndian(width));
        ihdr.AddRange(BigEndian(height));
        ihdr.AddRange([8, colorType, 0, 0, 0]);
        WriteChunk(png, "IHDR", [.. ihdr]);

        if (iccProfile is not null)
        {
            var iccp = new List<byte>(Encoding.ASCII.GetBytes("icc")) { 0, 0 };
            iccp.AddRange(ZlibCompress(iccProfile));
            WriteChunk(png, "iCCP", [.. iccp]);
        }

        WriteChunk(png, "IDAT", ZlibCompress(raw));
        WriteChunk(png, "IEND", []);
        return [.. png];
    }

    private static void WriteChunk(List<byte> png, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        byte[] crcInput = [.. typeBytes, .. data];
        png.AddRange(BigEndian(data.Length));
        png.AddRange(typeBytes);
        png.AddRange(data);
        png.AddRange(BigEndian(unchecked((int)Crc32(crcInput))));
    }

    private static byte[] BigEndian(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(
            output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }
        return output.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xffffffffu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
        }
        return crc ^ 0xffffffffu;
    }

    // A minimal but valid RGB matrix ICC profile with sRGB primaries and a LINEAR tone curve.
    // It is deliberately not sRGB, so ConvertToSrgb must transform the pixels and a profile
    // removal must be rejected (8.3).
    private static byte[] BuildLinearRgbIccProfile()
    {
        static byte[] Fixed16(double v) => BigEndian((int)Math.Round(v * 65536.0));

        static byte[] Xyz(double x, double y, double z) =>
            [.. Encoding.ASCII.GetBytes("XYZ "), 0, 0, 0, 0,
             .. Fixed16(x), .. Fixed16(y), .. Fixed16(z)];

        // 'curv' with zero entries denotes a linear (gamma 1.0) tone curve.
        byte[] linearCurve = [.. Encoding.ASCII.GetBytes("curv"), 0, 0, 0, 0, .. BigEndian(0)];

        (string Sig, byte[] Data)[] blocks =
        [
            ("wtpt", Xyz(0.9642, 1.0, 0.8249)),
            ("rXYZ", Xyz(0.43607, 0.22249, 0.01392)),
            ("gXYZ", Xyz(0.38515, 0.71687, 0.09708)),
            ("bXYZ", Xyz(0.14307, 0.06061, 0.71410)),
        ];

        const int tagCount = 7; // wtpt, rXYZ, gXYZ, bXYZ and three TRCs sharing one curve.
        var offset = 128 + 4 + (tagCount * 12);

        var data = new List<byte>();
        var table = new List<(string Sig, int Offset, int Size)>();
        foreach (var (sig, blob) in blocks)
        {
            table.Add((sig, offset, blob.Length));
            data.AddRange(blob);
            offset += blob.Length;
        }
        var curveOffset = offset;
        data.AddRange(linearCurve);
        offset += linearCurve.Length;
        foreach (var trc in new[] { "rTRC", "gTRC", "bTRC" })
        {
            table.Add((trc, curveOffset, linearCurve.Length));
        }

        var header = new byte[128];
        void PutSig(int at, string s) => Array.Copy(Encoding.ASCII.GetBytes(s), 0, header, at, 4);
        Array.Copy(BigEndian(offset), header, 4);       // total profile size
        Array.Copy(BigEndian(0x04300000), 0, header, 8, 4); // version 4.3
        PutSig(12, "mntr");
        PutSig(16, "RGB ");
        PutSig(20, "XYZ ");
        PutSig(36, "acsp");
        Array.Copy(Fixed16(0.9642), 0, header, 68, 4);  // PCS illuminant D50
        Array.Copy(Fixed16(1.0), 0, header, 72, 4);
        Array.Copy(Fixed16(0.8249), 0, header, 76, 4);

        var profile = new List<byte>(header);
        profile.AddRange(BigEndian(tagCount));
        foreach (var (sig, off, size) in table)
        {
            profile.AddRange(Encoding.ASCII.GetBytes(sig));
            profile.AddRange(BigEndian(off));
            profile.AddRange(BigEndian(size));
        }
        profile.AddRange(data);
        return [.. profile];
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
