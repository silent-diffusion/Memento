using Memento.Video.Export;

namespace Memento.Video.Sources;

/// <summary>
/// Turns one DirectShow buffer into NV12, the encoder's input (ENGINE-NOTES.md §R). YUV layouts are rearranged
/// without changing their values (4:2:2 chroma is averaged over each pair of rows); RGB is converted with the BT.709
/// limited-range matrix the rest of the video path uses (<c>Thumbnails.ToBitmap</c> is its inverse). Runs on the
/// camera's conversion thread, never on DirectShow's streaming thread. Pure, so every layout is unit-tested.
/// </summary>
internal static class RawFrameConverter
{
    /// <summary>
    /// Converts <paramref name="source"/> (one picture in <paramref name="format"/>) into <paramref name="target"/>, whose
    /// size must be <see cref="RawVideoFormat.EvenSize"/>. False when the buffer is too short or the layout unsupported.
    /// </summary>
    public static bool ToNv12(RawVideoFormat format, ReadOnlySpan<byte> source, Nv12Frame target)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(target);
        var (width, height) = format.EvenSize;
        if (format.Pixel == RawPixelFormat.Unsupported || width <= 0 || height <= 0 || source.Length < format.MinimumLength
            || target.Width != width || target.Height != height)
        {
            return false;
        }

        switch (format.Pixel)
        {
            case RawPixelFormat.Nv12:
                FromNv12(format, source, target);
                break;
            case RawPixelFormat.I420:
            case RawPixelFormat.Yv12:
                FromPlanar(format, source, target, vFirst: format.Pixel == RawPixelFormat.Yv12);
                break;
            case RawPixelFormat.Yuy2:
                FromPacked422(format, source, target, lumaFirst: true);
                break;
            case RawPixelFormat.Uyvy:
                FromPacked422(format, source, target, lumaFirst: false);
                break;
            default:
                FromRgb(format, source, target, format.Pixel == RawPixelFormat.Rgb32 ? 4 : 3);
                break;
        }

        return true;
    }

    private static void FromNv12(RawVideoFormat format, ReadOnlySpan<byte> source, Nv12Frame target)
    {
        var width = target.Width;
        var data = target.Data.AsSpan();
        for (var y = 0; y < target.Height; y++)
        {
            source.Slice(y * format.Stride, width).CopyTo(data.Slice(y * width, width));
        }

        var chroma = format.Stride * format.Height;
        var chromaTarget = width * target.Height;
        for (var y = 0; y < target.Height / 2; y++)
        {
            source.Slice(chroma + (y * format.Stride), width).CopyTo(data.Slice(chromaTarget + (y * width), width));
        }
    }

    private static void FromPlanar(RawVideoFormat format, ReadOnlySpan<byte> source, Nv12Frame target, bool vFirst)
    {
        var width = target.Width;
        var data = target.Data.AsSpan();
        for (var y = 0; y < target.Height; y++)
        {
            source.Slice(y * format.Stride, width).CopyTo(data.Slice(y * width, width));
        }

        var chromaStride = format.Stride / 2;
        var first = format.Stride * format.Height;
        var second = first + (chromaStride * format.ChromaRows);
        var u = vFirst ? second : first;
        var v = vFirst ? first : second;
        var chromaTarget = width * target.Height;
        for (var y = 0; y < target.Height / 2; y++)
        {
            var row = data.Slice(chromaTarget + (y * width), width);
            var uRow = source.Slice(u + (y * chromaStride), width / 2);
            var vRow = source.Slice(v + (y * chromaStride), width / 2);
            for (var x = 0; x < width / 2; x++)
            {
                row[2 * x] = uRow[x];
                row[(2 * x) + 1] = vRow[x];
            }
        }
    }

    private static void FromPacked422(RawVideoFormat format, ReadOnlySpan<byte> source, Nv12Frame target, bool lumaFirst)
    {
        var width = target.Width;
        var data = target.Data.AsSpan();
        var luma = lumaFirst ? 0 : 1;
        var chroma = lumaFirst ? 1 : 0;
        var chromaTarget = width * target.Height;
        for (var y = 0; y < target.Height; y += 2)
        {
            var top = source.Slice(y * format.Stride, width * 2);
            var bottom = source.Slice((y + 1) * format.Stride, width * 2);
            var topLuma = data.Slice(y * width, width);
            var bottomLuma = data.Slice((y + 1) * width, width);
            var uv = data.Slice(chromaTarget + (y / 2 * width), width);
            for (var x = 0; x < width; x++)
            {
                topLuma[x] = top[(2 * x) + luma];
                bottomLuma[x] = bottom[(2 * x) + luma];
            }

            // Each 4-byte group carries one U and one V for two pixels; average the two rows' chroma.
            for (var pair = 0; pair < width / 2; pair++)
            {
                var at = (4 * pair) + chroma;
                uv[2 * pair] = (byte)((top[at] + bottom[at] + 1) >> 1);
                uv[(2 * pair) + 1] = (byte)((top[at + 2] + bottom[at + 2] + 1) >> 1);
            }
        }
    }

    private static void FromRgb(RawVideoFormat format, ReadOnlySpan<byte> source, Nv12Frame target, int bytesPerPixel)
    {
        var width = target.Width;
        var height = target.Height;
        var data = target.Data.AsSpan();
        var chromaTarget = width * height;
        for (var y = 0; y < height; y += 2)
        {
            var top = Row(format, source, y);
            var bottom = Row(format, source, y + 1);
            var uv = data.Slice(chromaTarget + (y / 2 * width), width);
            for (var x = 0; x < width; x += 2)
            {
                int r = 0, g = 0, b = 0;
                for (var dy = 0; dy < 2; dy++)
                {
                    var row = dy == 0 ? top : bottom;
                    for (var dx = 0; dx < 2; dx++)
                    {
                        var p = (x + dx) * bytesPerPixel;
                        int pb = row[p], pg = row[p + 1], pr = row[p + 2];
                        data[((y + dy) * width) + x + dx] = Luma(pr, pg, pb);
                        r += pr;
                        g += pg;
                        b += pb;
                    }
                }

                // The 2 × 2 block's average colour, in 1/4 units: the coefficients are scaled by 256 and the sums by 4.
                uv[x] = Clamp(((-26 * r) - (87 * g) + (113 * b) + 512) >> 10, 128);
                uv[x + 1] = Clamp(((113 * r) - (103 * g) - (10 * b) + 512) >> 10, 128);
            }
        }
    }

    private static ReadOnlySpan<byte> Row(RawVideoFormat format, ReadOnlySpan<byte> source, int y)
    {
        var stored = format.BottomUp ? format.Height - 1 - y : y;
        return source.Slice(stored * format.Stride, format.Stride);
    }

    /// <summary>BT.709 limited range: Y = 16 + 0.1826 R + 0.6142 G + 0.0620 B (coefficients × 256).</summary>
    private static byte Luma(int r, int g, int b) => (byte)(16 + (((47 * r) + (157 * g) + (16 * b) + 128) >> 8));

    private static byte Clamp(int value, int offset) => (byte)Math.Clamp(value + offset, 16, 240);
}
