using PicCompressor.Application;
using PicCompressor.Domain;

namespace PicCompressor.Infrastructure;

public sealed class PhysicalInputImageInspector : IInputImageInspector
{
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

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

                return new InputImageInfo(InputImageFormat.Jpeg, width, height, stream.Length);
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
