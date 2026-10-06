namespace Memento.Core;

/// <summary>
/// Well-known locations under the per-user data root (<c>%LOCALAPPDATA%\Memento</c>).
/// Every value is resolved when read, so environment changes and test overrides of
/// <c>LOCALAPPDATA</c> take effect without restarting.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "Memento";

    /// <summary><c>%LOCALAPPDATA%\Memento</c>.</summary>
    public static string DataRoot => Path.Combine(LocalAppData(), AppFolderName);

    /// <summary>Default library location. Deliberately not Documents, which OneDrive often syncs.</summary>
    public static string DefaultLibrary => Path.Combine(DataRoot, "Library");

    public static string Logs => Path.Combine(DataRoot, "logs");

    public static string Models => Path.Combine(DataRoot, "models");

    public static string WebView2UserData => Path.Combine(DataRoot, "webview2");

    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    private static string LocalAppData()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
    }
}
