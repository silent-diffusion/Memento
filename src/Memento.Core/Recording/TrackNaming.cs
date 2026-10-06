using System.Text;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Recording;

/// <summary>
/// Track ids and capture file names: <c>mic</c>, <c>system</c>, <c>app-&lt;name&gt;</c>, with <c>-2</c>, <c>-3</c>
/// for repeats (a second microphone, or a source turned off and on again). Engines use this so names match everywhere.
/// </summary>
public static class TrackNaming
{
    private const int MaxNameLength = 24;

    /// <summary>Picks an id not in <paramref name="usedIds"/> and returns it with its file, <c>tracks/&lt;id&gt;.wav</c>.</summary>
    public static (string TrackId, string File) Allocate(AudioSource source, IReadOnlyCollection<string> usedIds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(usedIds);
        var stem = source.Kind switch
        {
            "microphone" => "mic",
            "system" => "system",
            "application" => "app-" + Slug(source.Name),
            _ => "track",
        };

        var id = stem;
        for (var n = 2; usedIds.Contains(id, StringComparer.OrdinalIgnoreCase); n++)
        {
            id = $"{stem}-{n}";
        }

        return (id, $"tracks/{id}.wav");
    }

    private static string Slug(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }

            if (builder.Length >= MaxNameLength)
            {
                break;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "app" : slug;
    }
}
