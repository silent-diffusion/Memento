using System.IO;
using System.Windows;
using System.Windows.Media;
using Memento.App.Bridge;
using Memento.App.Hosting;
using Memento.App.Theming;
using Memento.Core;
using Memento.Core.Library;
using Memento.Core.Settings;
using Memento.Core.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Memento.App;

/// <summary>
/// The only window: a WebView2 filling a standard Windows frame. Hosts the UI from
/// <c>&lt;app&gt;/ui</c> at <c>https://app.memento/</c> and wires the bridge, theme and screenshot mode.
/// </summary>
internal sealed partial class MainWindow : Window
{
    /// <summary>The design canvas size (renders' $preview); --screenshot renders the page at exactly this size.</summary>
    public static readonly Size ScreenshotSize = new(1440, 900);

    private static readonly TimeSpan ScreenshotTimeout = TimeSpan.FromSeconds(30);
    private static readonly Color LightGround = Color.FromRgb(0xEF, 0xED, 0xE8);
    private static readonly Color DarkGround = Color.FromRgb(0x1F, 0x1E, 0x1B);

    /// <summary>
    /// How the page may use library.memento. Not DenyCors: under DenyCors WebView2 answers every <c>fetch</c> from
    /// app.memento with an empty Access-Control-Allow-Origin (before WebResourceRequested could step in), so the page
    /// could play the mix but never read peaks.json. Allow is safe here because no other origin can exist in this
    /// WebView: top-level and frame navigations are limited to app.memento, new windows are refused, and the page CSP
    /// allows no frames. Range requests (seeking) work either way.
    /// </summary>
    private const CoreWebView2HostResourceAccessKind LibraryAccessKind = CoreWebView2HostResourceAccessKind.Allow;

#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif

    private readonly ThemeService _theme;
    private readonly WebViewBridge _bridge;
    private readonly FooterStatusService _footer;
    private readonly UiLifecycle _lifecycle;
    private readonly CommandLineOptions _options;
    private readonly ILibraryLocation _library;
    private readonly ILogger<MainWindow> _logger;
    private string? _mappedLibrary;

    public MainWindow(
        ThemeService theme,
        WebViewBridge bridge,
        FooterStatusService footer,
        UiLifecycle lifecycle,
        CommandLineOptions options,
        ILibraryLocation library,
        ISettingsStore settings,
        ILogger<MainWindow> logger)
    {
        _theme = theme;
        _bridge = bridge;
        _footer = footer;
        _lifecycle = lifecycle;
        _options = options;
        _library = library;
        _logger = logger;
        settings.Changed += (_, e) =>
        {
            if (!string.Equals(e.Previous.EffectiveLibraryPath, e.Current.EffectiveLibraryPath, StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(() => MapLibrary(WebView.CoreWebView2));
            }
        };

        InitializeComponent();
        ApplyTheme(theme.IsDark);
        theme.EffectiveThemeChanged += (_, isDark) => Dispatcher.BeginInvoke(() => ApplyTheme(isDark));

        if (options.IsScreenshotRun)
        {
            WebView.Width = ScreenshotSize.Width;
            WebView.Height = ScreenshotSize.Height;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = false;
        }
        else
        {
            FitToWorkArea();
        }

        Loaded += OnLoaded;
    }

    /// <summary>Brings the window forward when a second Memento is started.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (!await TryInitializeWebViewAsync())
        {
            return;
        }

        if (_options.IsScreenshotRun)
        {
            await CaptureScreenshotAndExitAsync(_options.ScreenshotPath!);
        }
    }

    private async Task<bool> TryInitializeWebViewAsync()
    {
        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        if (!File.Exists(Path.Combine(uiFolder, "index.html")))
        {
            LogUiMissing(uiFolder);
            Fail(
                $"Memento's interface files are missing from {uiFolder}. Reinstall Memento to restore them. Your recordings and settings were not touched.",
                exitCode: 2);
            return false;
        }

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: AppPaths.WebView2UserData);
            await WebView.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            LogRuntimeMissing(ex);
            Fail(
                "Memento needs the Microsoft Edge WebView2 Runtime, which is not installed on this PC. Run the Memento installer again to add it, or install it from Microsoft's WebView2 page. Nothing was changed.",
                exitCode: 2);
            return false;
        }

        var core = WebView.CoreWebView2;
        ConfigureSettings(core.Settings);
        core.Profile.PreferredColorScheme = _theme.IsDark
            ? CoreWebView2PreferredColorScheme.Dark
            : CoreWebView2PreferredColorScheme.Light;
        core.SetVirtualHostNameToFolderMapping(
            WebViewBridge.VirtualHost, uiFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        MapLibrary(core);
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationCompleted += OnNavigationCompleted;
        core.ProcessFailed += OnProcessFailed;
        _bridge.Attach(core, Dispatcher);
        core.Navigate(WebViewBridge.Origin + "index.html");
        return true;
    }

    /// <summary>
    /// Serves the library's <c>projects</c> folder (<see cref="LibraryUrls.MappedFolder"/>) at
    /// <c>https://library.memento/</c> so the page can stream the mix and read peaks. Only the project folders are
    /// reachable: <c>library.db</c> sits in the library root, above the mapped folder, and the browser resolves
    /// <c>..</c> segments before the path reaches the folder. Media and fetches from there are sub-resources of the
    /// app page, not navigations, so <see cref="OnNavigationStarting"/> still allows only <c>app.memento</c>.
    /// Remapped when the library moves.
    /// </summary>
    private void MapLibrary(CoreWebView2? core)
    {
        if (core is null)
        {
            return;
        }

        var folder = LibraryUrls.MappedFolder(_library.Root);
        if (string.Equals(folder, _mappedLibrary, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        if (_mappedLibrary is not null)
        {
            core.ClearVirtualHostNameToFolderMapping(LibraryUrls.VirtualHost);
        }

        core.SetVirtualHostNameToFolderMapping(LibraryUrls.VirtualHost, folder, LibraryAccessKind);
        _mappedLibrary = folder;
        LogLibraryMapped(folder);
    }

    private static void ConfigureSettings(CoreWebView2Settings settings)
    {
        settings.AreDevToolsEnabled = IsDebugBuild;
        settings.AreDefaultContextMenusEnabled = IsDebugBuild;
        settings.AreBrowserAcceleratorKeysEnabled = IsDebugBuild;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
    }

    private void ApplyTheme(bool isDark)
    {
        var ground = isDark ? DarkGround : LightGround;
        Background = new SolidColorBrush(ground);
        WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(ground.R, ground.G, ground.B);
        if (WebView.CoreWebView2 is { } core)
        {
            core.Profile.PreferredColorScheme = isDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.Uri.StartsWith(WebViewBridge.Origin, StringComparison.Ordinal))
        {
            e.Cancel = true;
            LogNavigationBlocked(e.Uri);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        LogNavigationBlocked(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            // The page subscribes to events while its scripts run, which is before this fires.
            _footer.Publish(force: true);
        }
        else
        {
            LogNavigationFailed(e.WebErrorStatus.ToString());
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        LogProcessFailed(e.ProcessFailedKind.ToString());
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            WebView.CoreWebView2?.Reload();
        }
    }

    private async Task CaptureScreenshotAndExitAsync(string path)
    {
        var ready = await Task.WhenAny(_lifecycle.Ready, Task.Delay(ScreenshotTimeout));
        if (ready != _lifecycle.Ready)
        {
            LogScreenshotTimedOut(ScreenshotTimeout.TotalSeconds);
            Application.Current.Shutdown(1);
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (var stream = File.Create(path))
            {
                await WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            }

            LogScreenshotSaved(path);
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogScreenshotFailed(ex, path);
            Application.Current.Shutdown(1);
        }
    }

    private void Fail(string message, int exitCode)
    {
        if (!_options.IsScreenshotRun)
        {
            MessageBox.Show(this, message, "Memento", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Application.Current.Shutdown(exitCode);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Project folders in {Folder} served at https://library.memento/")]
    private partial void LogLibraryMapped(string folder);

    [LoggerMessage(Level = LogLevel.Error, Message = "Interface files are missing from {Folder}")]
    private partial void LogUiMissing(string folder);

    [LoggerMessage(Level = LogLevel.Error, Message = "The WebView2 Runtime is not installed")]
    private partial void LogRuntimeMissing(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked navigation to {Uri}")]
    private partial void LogNavigationBlocked(string uri);

    [LoggerMessage(Level = LogLevel.Error, Message = "The interface failed to load: {Status}")]
    private partial void LogNavigationFailed(string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "A WebView2 process failed: {Kind}")]
    private partial void LogProcessFailed(string kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Screenshot: the interface did not report ready within {Seconds} s")]
    private partial void LogScreenshotTimedOut(double seconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Screenshot saved to {Path}")]
    private partial void LogScreenshotSaved(string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Screenshot could not be written to {Path}")]
    private partial void LogScreenshotFailed(Exception exception, string path);
}
