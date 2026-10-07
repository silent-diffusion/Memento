using System.Globalization;

namespace Memento.Documents.Model;

/// <summary>Formats <see cref="DocumentMeta"/> the same way in every output (invariant English, as the app's copy is).</summary>
public static class MetaLine
{
    public const string Separator = " · ";

    /// <summary>"Meeting minutes · Sunday 5 October 2026, 4:00 PM · 1 h 10 min · Zoom · 4 participants"; empty parts are left out.</summary>
    public static string Format(DocumentMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        var parts = new List<string>(5);
        Add(parts, meta.Kind);
        if (meta.RecordedAt is { } at)
        {
            parts.Add(LongDate(at));
        }

        if (meta.DurationMs is { } ms and > 0)
        {
            parts.Add(Duration(ms));
        }

        Add(parts, meta.Platform);
        if (meta.ParticipantCount is { } count and > 0)
        {
            parts.Add(count == 1 ? "1 participant" : string.Create(CultureInfo.InvariantCulture, $"{count} participants"));
        }

        return string.Join(Separator, parts);
    }

    /// <summary>"Sunday 5 October 2026, 4:00 PM" in the recording's own offset.</summary>
    public static string LongDate(DateTimeOffset at) => at.ToString("dddd d MMMM yyyy, h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>"5 October 2026", for the running header.</summary>
    public static string ShortDate(DateTimeOffset at) => at.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>"1 h 10 min", "42 min", "2 h", "35 s"; minutes to the nearest one, as the Library shows them (2:58 is "3 min").</summary>
    public static string Duration(long milliseconds)
    {
        var totalSeconds = milliseconds / 1000;
        if (totalSeconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{totalSeconds} s");
        }

        var totalMinutes = (totalSeconds + 30) / 60;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes} min")
            : minutes == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{hours} h")
                : string.Create(CultureInfo.InvariantCulture, $"{hours} h {minutes} min");
    }

    /// <summary>The running header's left and right parts: the recording title and its date.</summary>
    public static (string Left, string Right) RunningHeader(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var title = string.IsNullOrWhiteSpace(document.Meta.RecordingTitle) ? document.Title : document.Meta.RecordingTitle!;
        var date = document.Meta.RecordedAt is { } at ? ShortDate(at) : string.Empty;
        return (title, date);
    }

    private static void Add(List<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value.Trim());
        }
    }
}
