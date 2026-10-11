using System.Globalization;

namespace Memento.Core.Formatting;

/// <summary>
/// Settings › General › Date format (2.1): how a full date reads wherever Memento shows one (new titles, lists,
/// headers, exports, History). Times and timecodes are not affected. Every preset but <see cref="Windows"/> uses
/// English month names, as the rest of the interface does, so the text is the same on every PC.
/// </summary>
public static class DateFormats
{
    /// <summary>"2026 February 16" (the default).</summary>
    public const string YearMonthDay = "yearMonthDay";

    /// <summary>"16 February 2026".</summary>
    public const string DayMonthYear = "dayMonthYear";

    /// <summary>"February 16, 2026".</summary>
    public const string MonthDayYear = "monthDayYear";

    /// <summary>"2026-02-16".</summary>
    public const string Iso = "iso";

    /// <summary>The long date format chosen in Windows' region settings.</summary>
    public const string Windows = "windows";

    public const string Default = YearMonthDay;

    /// <summary>The presets in the order Settings lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [YearMonthDay, DayMonthYear, MonthDayYear, Iso, Windows];

    public static bool IsKnown(string? format) => format is not null && All.Contains(format, StringComparer.Ordinal);

    /// <summary>The date part of <paramref name="at"/> in <paramref name="format"/> (an unknown one reads as <see cref="Default"/>).</summary>
    /// <param name="windows">The culture "Windows setting" follows; the current one when <c>null</c>.</param>
    public static string Format(DateTimeOffset at, string? format, CultureInfo? windows = null) => format switch
    {
        DayMonthYear => at.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
        MonthDayYear => at.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
        Iso => at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Windows => at.ToString((windows ?? CultureInfo.CurrentCulture).DateTimeFormat.LongDatePattern, windows ?? CultureInfo.CurrentCulture),
        _ => at.ToString("yyyy MMMM d", CultureInfo.InvariantCulture),
    };

    /// <summary>A new recording's title when none is typed: "Untitled meeting · 2026 February 16".</summary>
    public static string UntitledTitle(string type, DateTimeOffset at, string? format, CultureInfo? windows = null) =>
        $"Untitled {type} · {Format(at, format, windows)}";
}
