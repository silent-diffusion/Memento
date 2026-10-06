using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace Memento.App.Theming;

/// <summary>
/// Reads the Windows app theme (Settings › Personalisation › Colours › "Choose your app mode") and
/// reports changes through <see cref="UISettings.ColorValuesChanged"/>. If the WinRT API is unavailable
/// it falls back to the <c>AppsUseLightTheme</c> registry value, read without watching.
/// </summary>
internal sealed partial class WindowsThemeWatcher : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly ILogger<WindowsThemeWatcher> _logger;
    private readonly UISettings? _uiSettings;

    public WindowsThemeWatcher(ILogger<WindowsThemeWatcher> logger)
    {
        _logger = logger;
        try
        {
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        }
#pragma warning disable CA1031 // Any WinRT activation failure means "use the registry instead".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogUiSettingsUnavailable(ex);
            _uiSettings = null;
        }
    }

    /// <summary>Raised on a background thread when Windows colours change (including, but not only, the app theme).</summary>
    public event EventHandler? Changed;

    public bool IsSystemDark => _uiSettings is not null ? IsDarkFromUiSettings(_uiSettings) : IsDarkFromRegistry();

    public void Dispose()
    {
        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        }
    }

    private void OnColorValuesChanged(UISettings sender, object args) => Changed?.Invoke(this, EventArgs.Empty);

    // Microsoft's guidance: the app theme is dark when the system foreground colour is light.
    private static bool IsDarkFromUiSettings(UISettings settings)
    {
        var foreground = settings.GetColorValue(UIColorType.Foreground);
        return (5 * foreground.G) + (2 * foreground.R) + foreground.B > 8 * 128;
    }

    private static bool IsDarkFromRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows UISettings is unavailable; reading the app theme from the registry without live updates")]
    private partial void LogUiSettingsUnavailable(Exception exception);
}
