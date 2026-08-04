namespace PicCompressor.Infrastructure.Tests;

/// <summary>
/// Baut minimale, aber strukturell gültige JPEG-Dateien für Inspektions- und
/// Veröffentlichungstests. Optional mit einem APP1-Segment, das nur die EXIF-Orientierung trägt.
/// </summary>
internal static class TestJpeg
{
    public static byte[] Create(
        int width,
        int height,
        int? orientation = null,
        bool bigEndian = false) =>
    [
        0xff, 0xd8,
        .. (orientation is { } value ? ExifApp1(value, bigEndian) : Array.Empty<byte>()),
        0xff, 0xc0, 0x00, 0x0b, 0x08,
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0x01, 0x01, 0x11, 0x00,
        0xff, 0xda, 0x00, 0x08,
        0x01, 0x01, 0x00, 0x00, 0x3f, 0x00,
        0x11, 0x22,
        0xff, 0xd9
    ];

    private static byte[] ExifApp1(int orientation, bool bigEndian)
    {
        byte[] tiff =
        [
            .. UInt16(bigEndian ? 0x4d4d : 0x4949, bigEndian: true),
            .. UInt16(0x2a, bigEndian),
            .. UInt32(8, bigEndian),
            .. UInt16(1, bigEndian),
            .. UInt16(0x0112, bigEndian),
            .. UInt16(3, bigEndian),
            .. UInt32(1, bigEndian),
            .. UInt16(orientation, bigEndian),
            0x00, 0x00,
            .. UInt32(0, bigEndian)
        ];

        var segmentLength = 2 + 6 + tiff.Length;
        return
        [
            0xff, 0xe1,
            (byte)(segmentLength >> 8), (byte)segmentLength,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
            .. tiff
        ];
    }

    private static byte[] UInt16(int value, bool bigEndian) =>
        bigEndian
            ? [(byte)(value >> 8), (byte)value]
            : [(byte)value, (byte)(value >> 8)];

    private static byte[] UInt32(int value, bool bigEndian) =>
        bigEndian
            ? [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]
            : [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];
}
