using System.Globalization;
using System.Text;

namespace Memento.Core.Attachments;

/// <summary>File names that are safe on Windows and never replace an existing file.</summary>
public static class FileNames
{
    public const int MaxStemLength = 80;

    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// <paramref name="name"/> without characters Windows refuses (<c>&lt;&gt;:"/\|?*</c>, control characters), runs of
    /// spaces collapsed, no leading or trailing dots and spaces, at most <see cref="MaxStemLength"/> characters, never
    /// a reserved device name; <paramref name="fallback"/> when nothing is left.
    /// </summary>
    public static string Sanitize(string? name, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder((name ?? string.Empty).Length);
        var lastWasSpace = false;
        foreach (var c in (name ?? string.Empty).Normalize(NormalizationForm.FormC))
        {
            var replaced = char.IsControl(c) || invalid.Contains(c) || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? ' ' : c;
            if (char.IsWhiteSpace(replaced))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(replaced);
            lastWasSpace = false;
        }

        var text = builder.ToString().Trim().Trim('.').Trim();
        if (text.Length > MaxStemLength)
        {
            text = text[..MaxStemLength].TrimEnd().TrimEnd('.');
        }

        if (text.Length == 0)
        {
            return fallback;
        }

        var stem = text.Split('.')[0];
        return Reserved.Contains(stem, StringComparer.OrdinalIgnoreCase) ? "_" + text : text;
    }

    /// <summary>A file name made safe while keeping its extension (lower-cased), e.g. <c>Agenda (final).DOCX</c> → <c>Agenda (final).docx</c>.</summary>
    public static string SanitizeKeepingExtension(string? name, string fallbackStem)
    {
        var extension = Path.GetExtension(name ?? string.Empty);
        if (extension.Length > 12 || extension.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.'))
        {
            extension = string.Empty;
        }

        var stem = Sanitize(Path.GetFileNameWithoutExtension(name ?? string.Empty), fallbackStem);
        return stem + extension.ToLowerInvariant();
    }

    /// <summary>
    /// <paramref name="name"/> in <paramref name="folder"/>, or <c>name (2).ext</c>, <c>name (3).ext</c>, … when a file or
    /// folder of that name exists or is in <paramref name="taken"/> (names reserved by the same job).
    /// </summary>
    public static string Unique(string folder, string name, ISet<string>? taken = null)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var candidate = name;
        for (var n = 2; Exists(folder, candidate, taken); n++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n}){extension}");
        }

        taken?.Add(candidate);
        return candidate;
    }

    private static bool Exists(string folder, string name, ISet<string>? taken)
    {
        var path = Path.Combine(folder, name);
        return (taken?.Contains(name) ?? false) || File.Exists(path) || Directory.Exists(path);
    }
}
