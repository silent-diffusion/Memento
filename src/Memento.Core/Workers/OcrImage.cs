namespace Memento.Core.Workers;

/// <summary>
/// One prepared image for Tesseract, sent on the worker's input as <c>{"type":"recognize","image":{…}}</c>: 8-bit gray
/// pixels, row by row, already decoded, scaled and straightened by the host (the worker never decodes a file).
/// </summary>
/// <param name="Id">Chosen by the host; the <c>recognized</c> line repeats it.</param>
/// <param name="Gray8">The <see cref="Width"/> × <see cref="Height"/> pixels, base64.</param>
public sealed record OcrImage(int Id, int Width, int Height, string Gray8)
{
    /// <summary>Encodes <paramref name="pixels"/> (one byte per pixel, rows top to bottom).</summary>
    public static OcrImage Create(int id, int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length != (long)width * height)
        {
            throw new ArgumentException("The pixel buffer does not match the image size.", nameof(pixels));
        }

        return new OcrImage(id, width, height, Convert.ToBase64String(pixels));
    }

    /// <summary>The pixels of <see cref="Gray8"/>.</summary>
    /// <exception cref="FormatException">Not base64, or not <see cref="Width"/> × <see cref="Height"/> bytes.</exception>
    public byte[] Decode()
    {
        var pixels = Convert.FromBase64String(Gray8);
        if (Width <= 0 || Height <= 0 || pixels.Length != (long)Width * Height)
        {
            throw new FormatException($"Image {Id} has {pixels.Length} bytes for {Width} × {Height} pixels.");
        }

        return pixels;
    }
}
