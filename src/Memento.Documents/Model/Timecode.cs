using System.Globalization;

namespace Memento.Documents.Model;

/// <summary>Formats transcript times the way DESIGN.md §2.2 does: <c>m:ss</c> under an hour, <c>h:mm:ss</c> from an hour.</summary>
public static class Timecode
{
    /// <summary><c>18:42</c>, <c>1:02:05</c>. Fractions of a second are dropped.</summary>
    public static string Format(double seconds)
    {
        var total = (long)Math.Floor(Math.Max(0, seconds));
        var h = total / 3600;
        var m = total / 60 % 60;
        var s = total % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}");
    }

    /// <summary>Always <c>h:mm:ss</c> (<c>0:18:42</c>), used in the footnote note "see the recording at …".</summary>
    public static string FormatLong(double seconds)
    {
        var total = (long)Math.Floor(Math.Max(0, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}");
    }

    /// <summary>The text of a timestamp footnote in Word and PDF: "18:42 — see the recording at 0:18:42".</summary>
    public static string FootnoteText(double seconds, string? display) =>
        $"{(string.IsNullOrWhiteSpace(display) ? Format(seconds) : display)} — see the recording at {FormatLong(seconds)}";

    /// <summary>Seconds in markup attributes: invariant, shortest round-trip form (<c>1122</c>, <c>12.4</c>).</summary>
    public static string ToAttribute(double seconds) => seconds.ToString("R", CultureInfo.InvariantCulture);

    public static bool TryParseAttribute(string? value, out double seconds) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && double.IsFinite(seconds) && seconds >= 0;
}
