using System.Globalization;
using System.Text;

namespace Memento.Documents.Model.Storage;

/// <summary>Ids of stored templates and styles: lower-case letters, digits and hyphens, so they are safe as file names.</summary>
public static class StoreIds
{
    public const int MaxLength = 64;

    public static bool IsValid(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxLength
        && (char.IsAsciiLetterLower(id[0]) || char.IsAsciiDigit(id[0]))
        && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    /// <summary>"Meeting minutes (copy)" → <c>meeting-minutes-copy</c>; never empty.</summary>
    public static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        var lastHyphen = true;
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            if (char.IsAsciiLetterLower(lower) || char.IsAsciiDigit(lower))
            {
                builder.Append(lower);
                lastHyphen = false;
            }
            else if (!lastHyphen)
            {
                builder.Append('-');
                lastHyphen = true;
            }

            if (builder.Length >= MaxLength - 4)
            {
                break;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "item" : slug;
    }

    /// <summary>The first of <c>base</c>, <c>base-2</c>, <c>base-3</c>… that <paramref name="taken"/> does not contain.</summary>
    public static string Unique(string baseId, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        if (!taken(baseId))
        {
            return baseId;
        }

        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{baseId}-{n}");
            if (!taken(candidate))
            {
                return candidate;
            }
        }
    }
}
