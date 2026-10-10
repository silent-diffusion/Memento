using System.IO;
using System.Windows;
using System.Windows.Media;
using Memento.App.Bridge;
using Memento.App.Hosting;
using Memento.App.Theming;
using Memento.Core;
using Memento.Core.Agendas;
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
    private bool _closed;
    private readonly FooterStatusService _footer;
    private readonly UiLifecycle _lifecycle;
    private readonly CommandLineOptions _options;
    private readonly ILibraryLocation _library;
    private readonly ILogger<MainWindow> _logger;
    private readonly DroppedFiles _dropped;
    private readonly TrayController _tray;
    private string? _mappedLibrary;

    public MainWindow(
        ThemeService theme,
        WebViewBridge bridge,
        FooterStatusService footer,
        UiLifecycle lifecycle,
        CommandLineOptions options,
        ILibraryLocation library,
        ISettingsStore settings,
        DroppedFiles dropped,
        LibraryOpener opener,
        TrayController tray,
        ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(opener);
        _tray = tray;
        opener.Opened += (_, _) => Dispatcher.BeginInvoke(() => MapLibrary(_closed ? null : WebView.CoreWebView2));
        _theme = theme;
        _bridge = bridge;
        _footer = footer;
        _lifecycle = lifecycle;
        _options = options;
        _library = library;
        _logger = logger;
        _dropped = dropped;
        settings.Changed += (_, e) =>
        {
            if (!string.Equals(e.Previous.EffectiveLibraryPath, e.Current.EffectiveLibraryPath, StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(() => MapLibrary(_closed ? null : WebView.CoreWebView2));
            }
        };

        InitializeComponent();
        ApplyTheme(theme.IsDark);
        theme.EffectiveThemeChanged += (_, isDark) => Dispatcher.BeginInvoke(() =>
        {
            if (!_closed)
            {
                ApplyTheme(isDark);
            }
        });

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
            if (options.StartInBackground)
            {
                // Started by the Windows startup entry: minimised, and hidden in the tray once the page has loaded
                // when "Keep running in the tray" is on (WebView2 needs a shown window to start).
                WindowState = WindowState.Minimized;
                ShowActivated = false;
            }
        }

        Loaded += OnLoaded;
    }

    /// <summary>With "Keep running in the tray" on, closing hides the window; recording and processing go on.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_options.IsScreenshotRun && _tray.KeepsRunning)
        {
            e.Cancel = true;
            Hide();
            LogHiddenToTray();
            _tray.OnHidden();
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// The WebView2 control is disposed with the window, but host services (recording levels, processing progress,
    /// the footer) keep publishing until the host has stopped. From here on nothing touches the control: a queued event
    /// posted to a disposed WebView2 throws on the UI thread and ended a normal close with a crash report.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _bridge.Detach();
        base.OnClosed(e);
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
        else if (_options.StartInBackground && _tray.KeepsRunning)
        {
            Hide();
            LogHiddenToTray();
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
            var environment = await WebViewEnvironmentFactory.CreateAsync();
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
        core.DownloadStarting += OnDownloadStarting;
        core.PermissionRequested += OnPermissionRequested;
        core.NavigationCompleted += OnNavigationCompleted;
        core.ProcessFailed += OnProcessFailed;
        core.WebMessageReceived += OnDroppedFiles; // before the bridge, so a drop's paths are known when its request runs
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
    /// Remapped when the library moves. WebView2 does not raise <c>WebResourceRequested</c> for a mapped host (verified
    /// in the 2026-10-07 security audit), so the mapping cannot be narrowed to the mix and peaks: every file under
    /// <c>projects</c>, and anything a junction there points at, is readable by the page (SA-02, accepted; the page
    /// can already read the same project data through the bridge).
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

        if (!Directory.Exists(_library.Root) && !LibraryAvailability.IsDefault(_library.Root))
        {
            // The library's drive is not connected (or the folder was moved): nothing to serve, and nothing is created
            // in its place. The page says so through library.list; the mapping follows once the library is back.
            LogLibraryNotMapped(_library.Root);
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

        // The page only ever loads app.memento and library.memento, so SmartScreen has nothing to check and would only
        // send page addresses to Microsoft (nothing leaves the PC without an explicit action). It applies to every
        // WebView2 on this user data folder, so the PDF printer turns it off too.
        settings.IsReputationCheckingRequired = false;
    }

    /// <summary>The page never downloads anything; exports are written by the host.</summary>
    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        e.Handled = true;
        LogNavigationBlocked(e.DownloadOperation.Uri);
    }

    /// <summary>
    /// The page needs no browser permission (microphone, camera, location, notifications, clipboard reading): audio is
    /// captured by the host. Refused without asking, so no WebView2 prompt can appear.
    /// </summary>
    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
        e.Handled = true;
        LogPermissionRefused(e.PermissionKind.ToString());
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

    /// <summary>
    /// A drop on the page arrives with the dropped <c>File</c> objects (<c>postMessageWithAdditionalObjects</c>); their
    /// real paths are kept for <c>agenda.importDropped</c>, which the same message carries. Only our page's messages count.
    /// </summary>
    private void OnDroppedFiles(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(WebViewBridge.Origin, StringComparison.Ordinal) || e.AdditionalObjects is not { Count: > 0 } objects)
        {
            return;
        }

        var paths = objects.OfType<CoreWebView2File>().Select(f => f.Path).ToList();
        if (paths.Count > 0)
        {
            _dropped.Register(paths);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "The window was hidden; Memento keeps running in the tray")]
    private partial void LogHiddenToTray();

    [LoggerMessage(Level = LogLevel.Information, Message = "Project folders in {Folder} served at https://library.memento/")]
    private partial void LogLibraryMapped(string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The library {Root} is not available; library.memento is not mapped")]
    private partial void LogLibraryNotMapped(string root);

    [LoggerMessage(Level = LogLevel.Error, Message = "Interface files are missing from {Folder}")]
    private partial void LogUiMissing(string folder);

    [LoggerMessage(Level = LogLevel.Error, Message = "The WebView2 Runtime is not installed")]
    private partial void LogRuntimeMissing(Exception exception);

    /// <summary>Logs where a blocked navigation pointed, by scheme and host only: its path or query could carry content.</summary>
    private void LogNavigationBlocked(string uri) =>
        LogNavigationBlockedTo(Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? parsed.IsUnc || parsed.HostNameType == UriHostNameType.Basic || string.IsNullOrEmpty(parsed.Host) ? parsed.Scheme + ":" : parsed.Scheme + "://" + parsed.Host
            : "an unparsable address");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked navigation to {Target}")]
    private partial void LogNavigationBlockedTo(string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a browser permission request: {Kind}")]
    private partial void LogPermissionRefused(string kind);

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
