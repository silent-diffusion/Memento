using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Memento.Core.Projects;

/// <summary>
/// Project ids: <c>yyyyMMdd-HHmmss-xxxxxx</c> in local time plus six random Crockford base32 characters
/// (lowercase, no i/l/o/u), e.g. <c>20261006-100000-k3f9ab</c>. Ids from the UI are always checked against
/// <see cref="IsValid"/> before they become a path, so a request can never name a folder outside the library.
/// </summary>
public static partial class ProjectId
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    public static string New(DateTimeOffset localTime)
    {
        Span<byte> random = stackalloc byte[6];
        RandomNumberGenerator.Fill(random);
        Span<char> suffix = stackalloc char[6];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[random[i] & 31];
        }

        return string.Create(CultureInfo.InvariantCulture, $"{localTime:yyyyMMdd-HHmmss}-{suffix}");
    }

    public static bool IsValid(string? id) => id is not null && Pattern().IsMatch(id);

    [GeneratedRegex("^[0-9]{8}-[0-9]{6}-[0-9a-hjkmnp-tv-z]{6}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
