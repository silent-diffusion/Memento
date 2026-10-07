using System.Text;

namespace Memento.Documents.Agenda.Text;

/// <summary>Decodes text files: a BOM decides; otherwise strict UTF-8, falling back to Windows-1252.</summary>
internal static class TextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes, out bool usedFallback)
    {
        usedFallback = false;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            usedFallback = true;
            return CodePagesEncodingProvider.Instance.GetEncoding(1252)?.GetString(bytes) ?? Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Whether bytes look like text rather than binary: decodable, and no NUL or control bytes.</summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return true;
        }

        var sample = bytes[..Math.Min(bytes.Length, 8192)];
        var control = 0;
        foreach (var b in sample)
        {
            if (b == 0)
            {
                return false;
            }

            if (b < 0x09 || (b > 0x0D && b < 0x20 && b != 0x1B))
            {
                control++;
            }
        }

        return control * 100 <= sample.Length;
    }

    public static AgendaParseWarning FallbackWarning(AgendaParseOptions options) => new(
        AgendaWarningCodes.EncodingFallback,
        $"{AgendaErrors.DisplayName(options)} is not saved as UTF-8, so it was read as Western European (Windows-1252) text. Check accented letters and symbols in the items.");
}
