using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.App.Theming;

/// <summary>
/// The effective theme. Priority: <c>--theme</c> on the command line, then Settings (light/dark),
/// then the Windows app theme. Pushes <c>theme.changed</c> to the page and raises
/// <see cref="EffectiveThemeChanged"/> for the window whenever the result changes.
/// </summary>
internal sealed partial class ThemeService : IThemeState, IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly WindowsThemeWatcher _windows;
    private readonly BridgeEventPublisher _publisher;
    private readonly CommandLineOptions _options;
    private readonly ILogger<ThemeService> _logger;
    private readonly object _gate = new();
    private bool _isDark;

    public ThemeService(
        ISettingsStore settings,
        WindowsThemeWatcher windows,
        BridgeEventPublisher publisher,
        CommandLineOptions options,
        ILogger<ThemeService> logger)
    {
        _settings = settings;
        _windows = windows;
        _publisher = publisher;
        _options = options;
        _logger = logger;
        _isDark = Resolve();
        _windows.Changed += OnInputsChanged;
        _settings.Changed += OnInputsChanged;
    }

    /// <summary>Raised (on any thread) with the new value after the effective theme flips.</summary>
    public event EventHandler<bool>? EffectiveThemeChanged;

    public bool IsDark
    {
        get
        {
            lock (_gate)
            {
                return _isDark;
            }
        }
    }

    public void Dispose()
    {
        _windows.Changed -= OnInputsChanged;
        _settings.Changed -= OnInputsChanged;
    }

    private void OnInputsChanged(object? sender, EventArgs e)
    {
        var next = Resolve();
        lock (_gate)
        {
            if (next == _isDark)
            {
                return;
            }

            _isDark = next;
        }

        LogThemeChanged(next ? "dark" : "light");
        _publisher.PublishThemeChanged(new ThemeChangedPayload(next));
        EffectiveThemeChanged?.Invoke(this, next);
    }

    private bool Resolve()
    {
        var preference = _options.ForcedTheme ?? _settings.Current.Theme;
        return preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => _windows.IsSystemDark,
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Effective theme is now {Theme}")]
    private partial void LogThemeChanged(string theme);
}
