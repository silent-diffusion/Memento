namespace Memento.Core.Settings;

/// <summary>Allowed values of <see cref="AppSettings.Theme"/>.</summary>
public static class ThemePreference
{
    public const string System = "system";
    public const string Light = "light";
    public const string Dark = "dark";

    public static IReadOnlyList<string> All { get; } = [System, Light, Dark];

    public static bool IsValid(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
}
