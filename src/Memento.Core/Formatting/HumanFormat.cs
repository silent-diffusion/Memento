using System.Globalization;

namespace Memento.Core.Formatting;

/// <summary>Sizes and times worded the way the interface words them, for messages and the History tab.</summary>
public static class HumanFormat
{
    /// <summary>"0 bytes", "12 KB", "412 MB", "1.4 GB" (binary units, as Windows Explorer shows them).</summary>
    public static string Bytes(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        const double gb = mb * 1024;
        return bytes switch
        {
            < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
            < (long)mb => string.Create(CultureInfo.InvariantCulture, $"{bytes / kb:0} KB"),
            < (long)gb => string.Create(CultureInfo.InvariantCulture, $"{bytes / mb:0} MB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / gb:0.0} GB"),
        };
    }

    /// <summary>A recording position or length: "0:42", "12:05", "1:02:03".</summary>
    public static string Clock(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes}:{time.Seconds:00}");
    }

    /// <summary>"1 track", "3 tracks".</summary>
    public static string Count(int count, string singular, string plural) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? singular : plural)}");
}
