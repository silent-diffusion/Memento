using System.Buffers.Binary;

namespace Memento.Video.Sources;

/// <summary>
/// Reads DirectShow media types: which subtype a picture has, and its size, row stride and frame rate from the
/// <c>VIDEOINFOHEADER</c> or <c>VIDEOINFOHEADER2</c> format block. Pure (bytes in, a <see cref="RawVideoFormat"/> out),
/// so it is unit-tested without a device.
/// </summary>
public static class DirectShowFormats
{
    public static readonly Guid Nv12 = FourCc("NV12");
    public static readonly Guid I420 = FourCc("I420");
    public static readonly Guid Iyuv = FourCc("IYUV");
    public static readonly Guid Yv12 = FourCc("YV12");
    public static readonly Guid Yuy2 = FourCc("YUY2");
    public static readonly Guid Yuyv = FourCc("YUYV");
    public static readonly Guid Uyvy = FourCc("UYVY");
    public static readonly Guid Mjpg = FourCc("MJPG");
    public static readonly Guid Rgb24 = new("e436eb7d-524f-11ce-9f53-0020af0ba770");
    public static readonly Guid Rgb32 = new("e436eb7e-524f-11ce-9f53-0020af0ba770");
    public static readonly Guid Argb32 = new("773c9ac0-3274-11d0-b724-00aa006c1a01");

    private const int VideoInfoBitmapOffset = 48;
    private const int VideoInfo2BitmapOffset = 72;
    private const int AvgTimePerFrameOffset = 40;
    private const int BitmapHeaderSize = 40;

    /// <summary>The bytes after the FourCC in every FourCC subtype GUID (<c>-0000-0010-8000-00AA00389B71</c>).</summary>
    private static readonly byte[] FourCcSuffix = [0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    private static readonly Guid FormatVideoInfo = new("05589f80-c356-11ce-bf01-00aa0055595a");
    private static readonly Guid FormatVideoInfo2 = new("F72A76A0-EB0A-11d0-ACE4-0000C0CC16BA");

    /// <summary>The subtypes the graph is asked to deliver, best first, when the camera's own format can't be read as it is.</summary>
    public static IReadOnlyList<Guid> Fallbacks { get; } = [Nv12, Yuy2, Rgb32, Rgb24];

    /// <summary>The media subtype GUID of a FourCC ("NV12" → <c>3231564E-0000-0010-8000-00AA00389B71</c>).</summary>
    public static Guid FourCc(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != 4)
        {
            throw new ArgumentException("A FourCC has four characters.", nameof(code));
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(System.Text.Encoding.ASCII.GetBytes(code));
        return new Guid(value, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
    }

    public static RawPixelFormat PixelOf(Guid subtype) =>
        subtype == Nv12 ? RawPixelFormat.Nv12
        : subtype == I420 || subtype == Iyuv ? RawPixelFormat.I420
        : subtype == Yv12 ? RawPixelFormat.Yv12
        : subtype == Yuy2 || subtype == Yuyv ? RawPixelFormat.Yuy2
        : subtype == Uyvy ? RawPixelFormat.Uyvy
        : subtype == Rgb24 ? RawPixelFormat.Rgb24
        : subtype == Rgb32 || subtype == Argb32 ? RawPixelFormat.Rgb32
        : RawPixelFormat.Unsupported;

    /// <summary>"NV12", "MJPG", "RGB32"; a FourCC subtype's own four letters, else the GUID.</summary>
    public static string NameOf(Guid subtype)
    {
        if (subtype == Rgb24)
        {
            return "RGB24";
        }

        if (subtype == Rgb32 || subtype == Argb32)
        {
            return "RGB32";
        }

        var bytes = subtype.ToByteArray();
        if (bytes.AsSpan(4).SequenceEqual(FourCcSuffix))
        {
            var letters = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
            if (letters.All(c => c is >= ' ' and <= '~'))
            {
                return letters.Trim();
            }
        }

        return subtype.ToString("B").ToUpperInvariant();
    }

    /// <summary>
    /// The picture format of a media type: <paramref name="formatBlock"/> is its <c>pbFormat</c> bytes. Null when the
    /// format block is not a video info header or is too short.
    /// </summary>
    public static RawVideoFormat? Parse(Guid subtype, Guid formatType, ReadOnlySpan<byte> formatBlock)
    {
        int offset;
        if (formatType == FormatVideoInfo)
        {
            offset = VideoInfoBitmapOffset;
        }
        else if (formatType == FormatVideoInfo2)
        {
            offset = VideoInfo2BitmapOffset;
        }
        else
        {
            return null;
        }

        if (formatBlock.Length < offset + BitmapHeaderSize)
        {
            return null;
        }

        var averageTimePerFrame = BinaryPrimitives.ReadInt64LittleEndian(formatBlock[AvgTimePerFrameOffset..]);
        var bitmap = formatBlock[offset..];
        var width = BinaryPrimitives.ReadInt32LittleEndian(bitmap[4..]);
        var storedHeight = BinaryPrimitives.ReadInt32LittleEndian(bitmap[8..]);
        var bitCount = BinaryPrimitives.ReadInt16LittleEndian(bitmap[14..]);
        if (width <= 0 || storedHeight == 0)
        {
            return null;
        }

        var pixel = PixelOf(subtype);
        var height = Math.Abs(storedHeight);
        var stride = pixel switch
        {
            RawPixelFormat.Rgb24 or RawPixelFormat.Rgb32 => ((width * (pixel == RawPixelFormat.Rgb24 ? 24 : 32)) + 31) / 32 * 4,
            RawPixelFormat.Yuy2 or RawPixelFormat.Uyvy => width * 2,
            RawPixelFormat.Unsupported => width * Math.Max(1, bitCount / 8),
            _ => width,
        };

        // RGB is bottom-up unless the height is negative; YUV is always top-down whatever the sign.
        var bottomUp = pixel is RawPixelFormat.Rgb24 or RawPixelFormat.Rgb32 && storedHeight > 0;
        var frameRate = averageTimePerFrame > 0 ? Math.Round(10_000_000.0 / averageTimePerFrame, 2) : 0;
        return new RawVideoFormat(pixel, width, height, stride, bottomUp, frameRate, NameOf(subtype));
    }

    /// <summary>A compressed subtype (MJPG, H.264 …) that needs a decoder in the graph.</summary>
    public static bool IsCompressed(Guid subtype) => PixelOf(subtype) == RawPixelFormat.Unsupported;
}
