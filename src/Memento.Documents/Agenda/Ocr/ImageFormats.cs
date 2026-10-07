using System.Buffers.Binary;

namespace Memento.Documents.Agenda.Ocr;

/// <summary>Recognizes image formats by their magic bytes and reads their pixel size from the header, without decoding.</summary>
internal static class ImageFormats
{
    public const string Png = "PNG";
    public const string Jpeg = "JPEG";
    public const string Bmp = "BMP";
    public const string Tiff = "TIFF";
    public const string Heic = "HEIC";
    public const string Gif = "GIF";
    public const string Webp = "WebP";

    /// <summary>The DIB header sizes Windows writes (core, info, v2, v3, v4, v5); anything else after "BM" is not a bitmap.</summary>
    private static readonly HashSet<uint> BmpHeaderSizes = [12, 40, 52, 56, 108, 124];

    public static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return Png;
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return Jpeg;
        }

        if (bytes.Length > 26 && bytes[0] == 'B' && bytes[1] == 'M' && BmpHeaderSizes.Contains(BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..])))
        {
            return Bmp;
        }

        if (bytes.StartsWith("II*\0"u8) || bytes.StartsWith("MM\0*"u8))
        {
            return Tiff;
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return Gif;
        }

        if (bytes.Length > 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        if (bytes.Length > 12 && bytes[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = System.Text.Encoding.ASCII.GetString(bytes[8..12]);
            if (brand is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "mif1" or "msf1")
            {
                return Heic;
            }
        }

        return null;
    }

    /// <summary>The pixel size from the header; <c>false</c> when the header does not say (the decoder checks then).</summary>
    public static bool TryGetSize(ReadOnlySpan<byte> bytes, out long width, out long height)
    {
        width = height = 0;
        try
        {
            switch (Detect(bytes))
            {
                case Png when bytes.Length >= 24:
                    width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]);
                    height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
                    return true;
                case Bmp when BinaryPrimitives.ReadUInt32LittleEndian(bytes[14..]) == 12:
                    // BITMAPCOREHEADER: 16-bit width and height.
                    width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]);
                    height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]);
                    return true;
                case Bmp:
                    // Signed 32-bit; a negative height means top-down. As long, so int.MinValue cannot overflow Math.Abs.
                    width = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]));
                    height = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]));
                    return true;
                case Gif when bytes.Length >= 10:
                    width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
                    height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
                    return true;
                case Jpeg:
                    return TryJpegSize(bytes, out width, out height);
                case Tiff:
                    return TryTiffSize(bytes, out width, out height);
                case Heic:
                    return TryHeicSize(bytes, out width, out height);
                default:
                    return false;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryJpegSize(ReadOnlySpan<byte> bytes, out long width, out long height)
    {
        width = height = 0;
        var i = 2;
        while (i + 9 < bytes.Length)
        {
            if (bytes[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = bytes[i + 1];
            if (marker is 0xFF)
            {
                i++;
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                i += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 2)..]);
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 5)..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 7)..]);
                return true;
            }

            i += 2 + length;
        }

        return false;
    }

    private static bool TryTiffSize(ReadOnlySpan<byte> bytes, out long width, out long height)
    {
        width = height = 0;
        var little = bytes[0] == 'I';
        var ifd = (int)U32(bytes, 4, little);
        var count = U16(bytes, ifd, little);
        for (var e = 0; e < count; e++)
        {
            var entry = ifd + 2 + (e * 12);
            var tag = U16(bytes, entry, little);
            var type = U16(bytes, entry + 2, little);
            long value = type == 3 ? U16(bytes, entry + 8, little) : U32(bytes, entry + 8, little);
            if (tag == 256)
            {
                width = value;
            }
            else if (tag == 257)
            {
                height = value;
            }
        }

        return width > 0 && height > 0;
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]) : BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]);

    private static ushort U16(ReadOnlySpan<byte> bytes, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]) : BinaryPrimitives.ReadUInt16BigEndian(bytes[at..]);

    private static bool TryHeicSize(ReadOnlySpan<byte> bytes, out long width, out long height)
    {
        width = height = 0;
        var window = bytes[..Math.Min(bytes.Length, 1 << 16)];
        var at = window.IndexOf("ispe"u8);
        if (at < 0 || at + 16 > window.Length)
        {
            return false;
        }

        width = BinaryPrimitives.ReadUInt32BigEndian(window[(at + 8)..]);
        height = BinaryPrimitives.ReadUInt32BigEndian(window[(at + 12)..]);
        return width > 0 && height > 0;
    }
}
