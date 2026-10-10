using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Memento.App.Hosting;

/// <summary>
/// The notification-area icon (Settings › General › Keep running in the tray, 2.0; off by default). While the setting
/// is on, the icon is shown with Open, Record and Quit, and closing the window only hides it: recording and processing
/// go on. A recording in progress is shown on the icon (an accent dot), in its tooltip and as the first line of its menu
/// (<see cref="TrayView"/>). Nothing here leaves the PC. Every method runs on the UI thread.
/// </summary>
internal sealed partial class TrayController(
    ISettingsStore settings,
    RecordingCoordinator recordings,
    IBridgeEventSink sink,
    ILogger<TrayController> logger) : IDisposable
{
    /// <summary>The accent token (design/tokens.css, light) for the recording dot.</summary>
    private static readonly Drawing.Color AccentDot = Drawing.Color.FromArgb(0xC2, 0x41, 0x0C);

    private readonly ILogger<TrayController> _logger = logger;
    private Forms.NotifyIcon? _icon;
    private Forms.ToolStripMenuItem? _status;
    private Forms.ToolStripSeparator? _statusSeparator;
    private Forms.ToolStripMenuItem? _open;
    private Forms.ToolStripMenuItem? _record;
    private Forms.ToolStripMenuItem? _quit;
    private Drawing.Icon? _plain;
    private Drawing.Icon? _recordingIcon;
    private DispatcherTimer? _timer;
    private Action? _show;
    private Action? _quitApp;
    private Dispatcher? _dispatcher;
    private bool _toldStillRunning;
    private TrayView? _shown;

    /// <summary>
    /// Whether closing the window should hide it: the setting is on, Quit was not chosen, and the icon is there to
    /// bring it back (a window is never hidden without a way to open it again).
    /// </summary>
    public bool KeepsRunning => settings.Current.General.KeepRunningInTray && !Quitting && _icon is not null;

    /// <summary>Quit was chosen (or Windows is ending the session): the next close really closes.</summary>
    public bool Quitting { get; private set; }

    /// <summary>Wires the icon to the window; shows it at once when the setting is on.</summary>
    /// <param name="show">Brings the window forward (also used by the single-instance guard).</param>
    /// <param name="quit">Closes the window for good.</param>
    public void Attach(Dispatcher dispatcher, Action show, Action quit)
    {
        _dispatcher = dispatcher;
        _show = show;
        _quitApp = quit;
        settings.Changed += OnSettingsChanged;
        Apply();
    }

    /// <summary>The window was hidden by a close: says once per run where Memento went.</summary>
    public void OnHidden()
    {
        if (_icon is null || _toldStillRunning)
        {
            return;
        }

        _toldStillRunning = true;
        var recording = TrayView.For(recordings.Current, System.Globalization.CultureInfo.CurrentCulture).IsRecording;
        _icon.ShowBalloonTip(5000, TrayView.StillRunningTitle, recording ? TrayView.StillRecordingBody : TrayView.StillRunningBody, Forms.ToolTipIcon.None);
    }

    /// <summary>Windows is signing out or shutting down: the window must close, not hide.</summary>
    public void QuitForSessionEnd() => Quitting = true;

    public void Dispose()
    {
        settings.Changed -= OnSettingsChanged;
        Hide();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Previous.General.KeepRunningInTray != e.Current.General.KeepRunningInTray)
        {
            _dispatcher?.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        if (settings.Current.General.KeepRunningInTray)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private void Show()
    {
        if (_icon is not null)
        {
            return;
        }

        try
        {
            _plain = LoadIcon();
            _recordingIcon = WithDot(_plain);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
            LogIconFailed(ex);
            return;
        }

        _status = new Forms.ToolStripMenuItem { Enabled = false, Visible = false };
        _statusSeparator = new Forms.ToolStripSeparator { Visible = false };
        _open = new Forms.ToolStripMenuItem("Open Memento", null, (_, _) => _show?.Invoke());
        _record = new Forms.ToolStripMenuItem("New recording", null, (_, _) => OpenRecord());
        _quit = new Forms.ToolStripMenuItem("Quit Memento", null, (_, _) => Quit());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([_status, _statusSeparator, _open, _record, new Forms.ToolStripSeparator(), _quit]);
        menu.Opening += (_, _) => Refresh();
        _icon = new Forms.NotifyIcon
        {
            Icon = _plain,
            Text = "Memento",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _show?.Invoke();
            }
        };
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), _dispatcher ?? Dispatcher.CurrentDispatcher);
        _timer.Start();
        Refresh();
        LogShown();
    }

    private void Hide()
    {
        _timer?.Stop();
        _timer = null;
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
            _icon = null;
            LogHidden();
        }

        _recordingIcon?.Dispose();
        _recordingIcon = null;
        _plain?.Dispose();
        _plain = null;
        _shown = null;
    }

    private void Refresh()
    {
        if (_icon is null)
        {
            return;
        }

        var view = TrayView.For(recordings.Current, System.Globalization.CultureInfo.CurrentCulture);
        if (view == _shown)
        {
            return;
        }

        _shown = view;
        _icon.Text = view.Tooltip;
        _icon.Icon = view.IsRecording ? _recordingIcon : _plain;
        _status!.Text = view.StatusLine ?? string.Empty;
        _status.Visible = view.StatusLine is not null;
        _statusSeparator!.Visible = view.StatusLine is not null;
        _open!.Text = view.OpenLabel;
        _record!.Text = view.RecordLabel;
        _quit!.Text = view.QuitLabel;
    }

    private void OpenRecord()
    {
        _show?.Invoke();
        sink.Post(BridgeEventPublisher.Serialize(
            BridgeEventNames.AppOpenScreen,
            new AppOpenScreenPayload(AppOpenScreenPayload.Record),
            LiveBridgeJsonContext.Default.BridgeEventEnvelopeAppOpenScreenPayload));
    }

    private void Quit()
    {
        Quitting = true;
        _quitApp?.Invoke();
    }

    private static Drawing.Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/memento.ico"))
            ?? throw new IOException("The Memento icon is missing from the program.");
        using var stream = resource.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>The icon with an accent dot in its lower right corner: a recording is in progress.</summary>
    private static Drawing.Icon WithDot(Drawing.Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        using (var brush = new Drawing.SolidBrush(AccentDot))
        using (var ring = new Drawing.Pen(Drawing.Color.White, Math.Max(1, bitmap.Width / 16f)))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var size = bitmap.Width * 0.5f;
            var rect = new Drawing.RectangleF(bitmap.Width - size - 0.5f, bitmap.Height - size - 0.5f, size, size);
            graphics.FillEllipse(brush, rect);
            graphics.DrawEllipse(ring, rect);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tray icon shown (keep running in the tray is on)")]
    private partial void LogShown();

    [LoggerMessage(Level = LogLevel.Information, Message = "Tray icon removed")]
    private partial void LogHidden();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The tray icon could not be made; closing the window closes Memento")]
    private partial void LogIconFailed(Exception exception);
}
