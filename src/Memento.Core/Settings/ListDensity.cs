namespace Memento.Core.Settings;

/// <summary>Allowed values of <see cref="AppSettings.ListDensity"/> (Settings › General › List density).</summary>
public static class ListDensity
{
    public const string Comfortable = "comfortable";
    public const string Compact = "compact";

    public static IReadOnlyList<string> All { get; } = [Comfortable, Compact];

    public static bool IsValid(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}
