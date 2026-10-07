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

    /// <summary>Rotates clockwise by <paramref name="degrees"/> around the centre onto a canvas that fits the whole image, white outside.</summary>
    public GrayImage Rotate(double degrees, CancellationToken cancellationToken)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var width = (int)Math.Ceiling((Math.Abs(Width * cos) + Math.Abs(Height * sin)) - 0.001);
        var height = (int)Math.Ceiling((Math.Abs(Width * sin) + Math.Abs(Height * cos)) - 0.001);
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
