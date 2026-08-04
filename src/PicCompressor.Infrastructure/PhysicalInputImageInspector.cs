using System.Buffers.Binary;
using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Infrastructure;

public sealed class PhysicalInputImageInspector : IInputImageInspector
{
    private const int OrientationIdentity = 1;

    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private static ReadOnlySpan<byte> ExifSignature =>
        [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00];

    public InputImageInfo Inspect(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);

            Span<byte> signature = stackalloc byte[8];
            stream.ReadExactly(signature);
            stream.Position = 0;

            if (signature.SequenceEqual(PngSignature))
            {
                return InspectPng(stream);
            }

            if (signature[0] == 0xff && signature[1] == 0xd8)
            {
                return InspectJpeg(stream);
            }

            throw InvalidImage();
        }
        catch (EndOfStreamException exception)
        {
            throw InvalidImage(exception);
        }
    }

    private static InputImageInfo InspectPng(Stream stream)
    {
        Span<byte> signature = stackalloc byte[8];
        stream.ReadExactly(signature);

        var width = 0;
        var height = 0;
        var firstChunk = true;
        var sawImageData = false;

        while (stream.Position < stream.Length)
        {
            var length = ReadUInt32(stream);
            var type = ReadUInt32(stream);
            var remaining = stream.Length - stream.Position;
            if ((long)length + 4 > remaining)
            {
                throw InvalidImage();
            }

            if (firstChunk)
            {
                if (type != 0x49484452 || length != 13)
                {
                    throw InvalidImage();
                }

                width = ReadDimension(stream);
                height = ReadDimension(stream);
            }

            if (type == 0x49444154)
            {
                sawImageData = true;
            }

            stream.Position += length - (firstChunk ? 8 : 0);
            stream.Position += 4;

            if (type == 0x49454e44)
            {
                if (length != 0
                    || !sawImageData
                    || width == 0
                    || height == 0
                    || stream.Position != stream.Length)
                {
                    throw InvalidImage();
                }

                return new InputImageInfo(InputImageFormat.Png, width, height, stream.Length);
            }

            firstChunk = false;
        }

        throw InvalidImage();
    }

    private static InputImageInfo InspectJpeg(Stream stream)
    {
        if (stream.ReadByte() != 0xff || stream.ReadByte() != 0xd8)
        {
            throw InvalidImage();
        }

        var width = 0;
        var height = 0;
        var inScan = false;
        var sawScan = false;
        var alreadyOptimized = false;
        var orientation = OrientationIdentity;

        while (stream.Position < stream.Length)
        {
            var marker = ReadJpegMarker(stream, inScan);
            if (marker == 0x00)
            {
                continue;
            }

            if (marker == 0xd9)
            {
                if (!sawScan || width == 0 || height == 0)
                {
                    throw InvalidImage();
                }

                // Der Encoder dreht die Pixel gemäss EXIF-Orientierung aufrecht (8.2), also
                // meldet der Inspector die aufrechten Maße. Sonst verwirft die
                // Ausgabeprüfung jedes gedrehte Bild als Dimensionsabweichung.
                var swapAxes = orientation >= 5;
                return new InputImageInfo(
                    InputImageFormat.Jpeg,
                    swapAxes ? height : width,
                    swapAxes ? width : height,
                    stream.Length,
                    alreadyOptimized);
            }

            if (marker is >= 0xd0 and <= 0xd7)
            {
                continue;
            }

            inScan = false;
            var segmentLength = ReadUInt16(stream);
            if (segmentLength < 2 || segmentLength - 2 > stream.Length - stream.Position)
            {
                throw InvalidImage();
            }

            var payloadLength = segmentLength - 2;
            if (IsStartOfFrame(marker))
            {
                if (payloadLength < 6)
                {
                    throw InvalidImage();
                }

                _ = stream.ReadByte();
                height = ReadUInt16(stream);
                width = ReadUInt16(stream);
                if (width == 0 || height == 0)
                {
                    throw InvalidImage();
                }

                stream.Position += payloadLength - 5;
            }
            else if (marker == 0xfe)
            {
                // Kommentarsegment auf den Provenienz-Marker prüfen (Issue #1). Der Marker liegt
                // im Kopfbereich, ein voller Datei-Ladevorgang ist dafür nicht nötig.
                var payload = new byte[payloadLength];
                stream.ReadExactly(payload);
                if (JpegOptimizationMarker.PayloadContainsToken(payload))
                {
                    alreadyOptimized = true;
                }
            }
            else if (marker == 0xe1 && orientation == OrientationIdentity)
            {
                var payload = new byte[payloadLength];
                stream.ReadExactly(payload);
                orientation = ReadExifOrientation(payload);
            }
            else
            {
                stream.Position += payloadLength;
            }

            if (marker == 0xda)
            {
                sawScan = true;
                inScan = true;
            }
        }

        throw InvalidImage();
    }

    private static int ReadJpegMarker(Stream stream, bool inScan)
    {
        while (true)
        {
            var value = stream.ReadByte();
            if (value < 0)
            {
                throw new EndOfStreamException();
            }

            if (value != 0xff)
            {
                if (inScan)
                {
                    continue;
                }

                throw InvalidImage();
            }

            do
            {
                value = stream.ReadByte();
            }
            while (value == 0xff);

            if (value < 0)
            {
                throw new EndOfStreamException();
            }

            if (inScan && value == 0x00)
            {
                continue;
            }

            return value;
        }
    }

    private static bool IsStartOfFrame(int marker) =>
        marker is >= 0xc0 and <= 0xcf
        && marker is not 0xc4 and not 0xc8 and not 0xcc;

    /// <summary>
    /// Liest EXIF-Tag 0x0112 aus IFD0 der APP1-Nutzlast. Fehlende, fremde oder unbrauchbare
    /// Daten ergeben die Normalstellung; die Nutzlast gilt durchgehend als nicht vertrauenswürdig.
    /// </summary>
    private static int ReadExifOrientation(ReadOnlySpan<byte> payload)
    {
        if (!payload.StartsWith(ExifSignature))
        {
            return OrientationIdentity;
        }

        var tiff = payload[ExifSignature.Length..];
        if (tiff.Length < 8)
        {
            return OrientationIdentity;
        }

        bool bigEndian;
        if (tiff[0] == 0x49 && tiff[1] == 0x49)
        {
            bigEndian = false;
        }
        else if (tiff[0] == 0x4d && tiff[1] == 0x4d)
        {
            bigEndian = true;
        }
        else
        {
            return OrientationIdentity;
        }

        if (ReadUInt16(tiff[2..], bigEndian) != 0x2a)
        {
            return OrientationIdentity;
        }

        // Offsets in 64 Bit rechnen: eine gefälschte 32-Bit-Angabe darf beim Grenzvergleich
        // nicht überlaufen und so einen negativen Index erzeugen.
        long ifd0 = ReadUInt32(tiff[4..], bigEndian);
        if (ifd0 < 8 || ifd0 + 2 > tiff.Length)
        {
            return OrientationIdentity;
        }

        var entryCount = ReadUInt16(tiff[(int)ifd0..], bigEndian);
        for (var index = 0; index < entryCount; ++index)
        {
            var entry = ifd0 + 2 + ((long)index * 12);
            if (entry + 12 > tiff.Length)
            {
                return OrientationIdentity;
            }

            var field = tiff[(int)entry..];
            if (ReadUInt16(field, bigEndian) != 0x0112)
            {
                continue;
            }

            if (ReadUInt16(field[2..], bigEndian) != 3
                || ReadUInt32(field[4..], bigEndian) != 1)
            {
                return OrientationIdentity;
            }

            var value = ReadUInt16(field[8..], bigEndian);
            return value is >= 1 and <= 8 ? value : OrientationIdentity;
        }

        return OrientationIdentity;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(source)
            : BinaryPrimitives.ReadUInt16LittleEndian(source);

    private static uint ReadUInt32(ReadOnlySpan<byte> source, bool bigEndian) =>
        bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(source)
            : BinaryPrimitives.ReadUInt32LittleEndian(source);

    private static int ReadDimension(Stream stream)
    {
        var value = ReadUInt32(stream);
        if (value is 0 or > int.MaxValue)
        {
            throw InvalidImage();
        }

        return (int)value;
    }

    private static ushort ReadUInt16(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[2];
        stream.ReadExactly(bytes);
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return ((uint)bytes[0] << 24)
            | ((uint)bytes[1] << 16)
            | ((uint)bytes[2] << 8)
            | bytes[3];
    }

    private static InvalidDataException InvalidImage(Exception? innerException = null) =>
        new("Input is not a structurally valid JPEG or PNG file.", innerException);
}
