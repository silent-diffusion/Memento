namespace Memento.Documents.Agenda.Ocr;

/// <summary>An 8-bit grayscale image with the two operations OCR preparation needs: scaling and rotation.</summary>
internal sealed class GrayImage
{
    public GrayImage(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height)
        {
            throw new ArgumentException("The pixel buffer does not match the image size.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>From premultiplied BGRA8, composited onto white so transparent backgrounds read as paper.</summary>
    public static GrayImage FromPremultipliedBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        var pixels = new byte[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var b = bgra[i * 4];
            var g = bgra[(i * 4) + 1];
            var r = bgra[(i * 4) + 2];
            var a = bgra[(i * 4) + 3];
            var luma = ((r * 77) + (g * 150) + (b * 29)) >> 8;
            pixels[i] = (byte)Math.Min(255, luma + (255 - a));
        }

        return new GrayImage(width, height, pixels);
    }

    public byte[] ToBgra()
    {
        var bgra = new byte[Pixels.Length * 4];
        for (var i = 0; i < Pixels.Length; i++)
        {
            var v = Pixels[i];
            bgra[i * 4] = v;
            bgra[(i * 4) + 1] = v;
            bgra[(i * 4) + 2] = v;
            bgra[(i * 4) + 3] = 255;
        }

        return bgra;
    }

    /// <summary>Bilinear scaling by <paramref name="factor"/>.</summary>
    public GrayImage Scale(double factor, CancellationToken cancellationToken)
    {
        var width = Math.Max(1, (int)Math.Round(Width * factor));
        var height = Math.Max(1, (int)Math.Round(Height * factor));
        var pixels = new byte[width * height];
        var inverse = 1.0 / factor;
        for (var y = 0; y < height; y++)
        {
            if ((y & 127) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var sourceY = ((y + 0.5) * inverse) - 0.5;
            for (var x = 0; x < width; x++)
            {
                var sourceX = ((x + 0.5) * inverse) - 0.5;
                pixels[(y * width) + x] = Sample(sourceX, sourceY);
            }
        }

        return new GrayImage(width, height, pixels);
    }

    /// <summary>The canvas <see cref="Rotate"/> would allocate, so a caller can refuse it before any memory is taken.</summary>
    public static (int Width, int Height) RotatedSize(int width, int height, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        return (
            (int)Math.Ceiling((width * cos) + (height * sin) - 0.001),
            (int)Math.Ceiling((width * sin) + (height * cos) - 0.001));
    }

    /// <summary>Rotates clockwise by <paramref name="degrees"/> around the centre onto a canvas that fits the whole image, white outside.</summary>
    public GrayImage Rotate(double degrees, CancellationToken cancellationToken)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var (width, height) = RotatedSize(Width, Height, degrees);
        var pixels = new byte[width * height];
        var centerX = (Width - 1) / 2.0;
        var centerY = (Height - 1) / 2.0;
        var outCenterX = (width - 1) / 2.0;
        var outCenterY = (height - 1) / 2.0;
        for (var y = 0; y < height; y++)
        {
            if ((y & 127) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var dy = y - outCenterY;
            for (var x = 0; x < width; x++)
            {
                // Inverse mapping: rotate the output point back by -degrees to find where it came from (y grows downwards).
                var dx = x - outCenterX;
                var sourceX = centerX + (dx * cos) + (dy * sin);
                var sourceY = centerY - (dx * sin) + (dy * cos);
                pixels[(y * width) + x] = Sample(sourceX, sourceY);
            }
        }

        return new GrayImage(width, height, pixels);
    }

    /// <summary>
    /// Evens out the paper (Tesseract, ENGINE-NOTES.md §P): the background brightness is estimated per
    /// <paramref name="tile"/>-pixel tile (its 90th percentile, then the brightest of each tile's neighbours, so a tile
    /// full of ink takes its paper from around it) and interpolated between tile centres, and every pixel is divided by
    /// it. Warm or grey paper, a shadow, or a photo of a page placed on a white PDF page all become white paper with dark
    /// ink, which Tesseract's global threshold and page layout analysis need.
    /// </summary>
    public GrayImage NormalizeBackground(int tile, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tile, 4);
        var columns = (Width + tile - 1) / tile;
        var rows = (Height + tile - 1) / tile;
        var level = new double[columns * rows];
        var histogram = new int[256];
        for (var ty = 0; ty < rows; ty++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var tx = 0; tx < columns; tx++)
            {
                Array.Clear(histogram);
                var x0 = tx * tile;
                var y0 = ty * tile;
                var x1 = Math.Min(Width, x0 + tile);
                var y1 = Math.Min(Height, y0 + tile);
                for (var y = y0; y < y1; y++)
                {
                    var row = y * Width;
                    for (var x = x0; x < x1; x++)
                    {
                        histogram[Pixels[row + x]]++;
                    }
                }

                var wanted = (x1 - x0) * (y1 - y0) * 0.9;
                var seen = 0;
                var value = 255;
                for (var v = 0; v < 256; v++)
                {
                    seen += histogram[v];
                    if (seen >= wanted)
                    {
                        value = v;
                        break;
                    }
                }

                level[(ty * columns) + tx] = value;
            }
        }

        var background = new double[level.Length];
        for (var ty = 0; ty < rows; ty++)
        {
            for (var tx = 0; tx < columns; tx++)
            {
                var brightest = 0.0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = tx + dx;
                        var ny = ty + dy;
                        if (nx >= 0 && ny >= 0 && nx < columns && ny < rows)
                        {
                            brightest = Math.Max(brightest, level[(ny * columns) + nx]);
                        }
                    }
                }

                background[(ty * columns) + tx] = Math.Max(1, brightest);
            }
        }

        var pixels = new byte[Pixels.Length];
        for (var y = 0; y < Height; y++)
        {
            if ((y & 127) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var gy = Math.Clamp(((y + 0.5) / tile) - 0.5, 0, rows - 1);
            var ty0 = (int)gy;
            var ty1 = Math.Min(rows - 1, ty0 + 1);
            var fy = gy - ty0;
            for (var x = 0; x < Width; x++)
            {
                var gx = Math.Clamp(((x + 0.5) / tile) - 0.5, 0, columns - 1);
                var tx0 = (int)gx;
                var tx1 = Math.Min(columns - 1, tx0 + 1);
                var fx = gx - tx0;
                var top = background[(ty0 * columns) + tx0] + ((background[(ty0 * columns) + tx1] - background[(ty0 * columns) + tx0]) * fx);
                var bottom = background[(ty1 * columns) + tx0] + ((background[(ty1 * columns) + tx1] - background[(ty1 * columns) + tx0]) * fx);
                var paper = top + ((bottom - top) * fy);
                pixels[(y * Width) + x] = (byte)Math.Clamp(Math.Round(Pixels[(y * Width) + x] * 255.0 / paper), 0, 255);
            }
        }

        return new GrayImage(Width, Height, pixels);
    }

    private byte Sample(double x, double y)
    {
        if (x < -0.5 || y < -0.5 || x > Width - 0.5 || y > Height - 0.5)
        {
            return 255;
        }

        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var p00 = At(x0, y0);
        var p10 = At(x0 + 1, y0);
        var p01 = At(x0, y0 + 1);
        var p11 = At(x0 + 1, y0 + 1);
        var top = p00 + ((p10 - p00) * fx);
        var bottom = p01 + ((p11 - p01) * fx);
        return (byte)Math.Clamp(Math.Round(top + ((bottom - top) * fy)), 0, 255);
    }

    private double At(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        return Pixels[(y * Width) + x];
    }
}
