namespace Memento.Video.Export;

/// <summary>
/// One picture in NV12 (a full-size luma plane, then interleaved half-size chroma) in managed memory, for composing
/// export frames on the processor: copying a decoded picture in, drawing a scaled picture into a rectangle (the camera
/// inset, or a letterboxed picture of another size) and filling black. Width and height are even.
/// </summary>
internal sealed class Nv12Frame
{
    public Nv12Frame(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width & 1) != 0 || (height & 1) != 0)
        {
            throw new ArgumentException($"An NV12 picture needs an even, positive size, not {width} × {height}.");
        }

        Width = width;
        Height = height;
        Data = new byte[width * height * 3 / 2];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Luma rows of <see cref="Width"/> bytes, then <see cref="Height"/>/2 chroma rows of <see cref="Width"/> bytes (U, V pairs).</summary>
    public byte[] Data { get; }

    private int ChromaOffset => Width * Height;

    /// <summary>BT.709 limited-range black: Y 16, U and V 128.</summary>
    public void FillBlack()
    {
        Data.AsSpan(0, ChromaOffset).Fill(16);
        Data.AsSpan(ChromaOffset).Fill(128);
    }

    /// <summary>Copies a decoded picture of this frame's size (or larger: the excess rows and columns are cut).</summary>
    /// <param name="source">The luma plane's first byte.</param>
    /// <param name="pitch">Bytes per row in the source.</param>
    /// <param name="sourceHeight">Rows of the source's luma plane (the chroma plane follows them).</param>
    public unsafe void CopyFrom(IntPtr source, int pitch, int sourceWidth, int sourceHeight)
    {
        var rows = Math.Min(Height, sourceHeight & ~1);
        var columns = Math.Min(Width, sourceWidth & ~1);
        var bytes = (byte*)source;
        fixed (byte* target = Data)
        {
            for (var y = 0; y < rows; y++)
            {
                Buffer.MemoryCopy(bytes + ((long)y * pitch), target + ((long)y * Width), Width, columns);
            }

            var chroma = bytes + ((long)pitch * sourceHeight);
            for (var y = 0; y < rows / 2; y++)
            {
                Buffer.MemoryCopy(chroma + ((long)y * pitch), target + ChromaOffset + ((long)y * Width), Width, columns);
            }
        }
    }

    /// <summary>Draws <paramref name="source"/> scaled (nearest neighbour) into the rectangle; parts outside this frame are cut.</summary>
    public void DrawScaled(Nv12Frame source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        x &= ~1;
        y &= ~1;
        width &= ~1;
        height &= ~1;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var src = source.Data;
        var dst = Data;
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(Width, x + width);
        var y1 = Math.Min(Height, y + height);
        if (x0 >= x1 || y0 >= y1)
        {
            return;
        }

        var map = new int[x1 - x0];
        for (var dx = x0; dx < x1; dx++)
        {
            map[dx - x0] = (int)((long)(dx - x) * source.Width / width);
        }

        for (var dy = y0; dy < y1; dy++)
        {
            var sy = (int)((long)(dy - y) * source.Height / height);
            var sRow = sy * source.Width;
            var dRow = dy * Width;
            for (var i = 0; i < map.Length; i++)
            {
                dst[dRow + x0 + i] = src[sRow + map[i]];
            }
        }

        // Chroma: one U, V pair per 2 × 2 block.
        var sChroma = source.ChromaOffset;
        var dChroma = ChromaOffset;
        for (var dy = y0 / 2; dy < y1 / 2; dy++)
        {
            var sy = (int)((long)((dy * 2) - y) * source.Height / height) / 2;
            var sRow = sChroma + (sy * source.Width);
            var dRow = dChroma + (dy * Width);
            for (var dx = x0 / 2; dx < x1 / 2; dx++)
            {
                var sx = ((int)((long)((dx * 2) - x) * source.Width / width) / 2) * 2;
                dst[dRow + (dx * 2)] = src[sRow + sx];
                dst[dRow + (dx * 2) + 1] = src[sRow + sx + 1];
            }
        }
    }

    /// <summary>Draws <paramref name="source"/> as large as fits, centred, keeping its shape (the rest stays as it is).</summary>
    public void DrawLetterboxed(Nv12Frame source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Width == Width && source.Height == Height)
        {
            source.Data.CopyTo(Data, 0);
            return;
        }

        var scale = Math.Min((double)Width / source.Width, (double)Height / source.Height);
        var w = (int)(source.Width * scale) & ~1;
        var h = (int)(source.Height * scale) & ~1;
        FillBlack();
        DrawScaled(source, (Width - w) / 2, (Height - h) / 2, w, h);
    }

    /// <summary>The camera inset's rectangle in a <paramref name="width"/> × <paramref name="height"/> picture: a quarter of the width, a margin of a fortieth.</summary>
    public static (int X, int Y, int Width, int Height) InsetRectangle(int width, int height, int insetWidth, int insetHeight, string corner)
    {
        var w = Math.Max(2, (width / 4) & ~1);
        var h = Math.Max(2, (int)((long)w * Math.Max(1, insetHeight) / Math.Max(1, insetWidth)) & ~1);
        var margin = Math.Max(2, (width / 40) & ~1);
        var left = corner is Core.Video.VideoPipChange.TopLeft or Core.Video.VideoPipChange.BottomLeft;
        var top = corner is Core.Video.VideoPipChange.TopLeft or Core.Video.VideoPipChange.TopRight;
        return (left ? margin : width - w - margin, top ? margin : height - h - margin, w, h);
    }
}
