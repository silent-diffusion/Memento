using System.Runtime.InteropServices;
using Memento.Audio;
using Memento.Video.Gpu;
using Memento.Video.Interop;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace Memento.Video.Sources;

/// <summary>
/// A display or a window through <c>Windows.Graphics.Capture</c> (ENGINE-NOTES.md §M.1). Each picture is copied on the
/// GPU into one texture this source owns, so the frame pool's buffers go straight back to Windows; the encoder reads
/// that texture at its own pace. Frames carry <c>Direct3D11CaptureFrame.SystemRelativeTime</c>, which is QPC time in
/// 100 ns units, the clock the audio tracks use.
/// </summary>
public sealed class GraphicsCaptureSource : IVideoSource
{
    private readonly VideoDevice _device;
    private readonly GraphicsCaptureOptions _options;
    private readonly IntPtr _monitor;
    private readonly IntPtr _window;
    private readonly object _gate = new();
    private readonly List<ID3D11Texture2D> _retired = [];

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _latest;
    private int _latestWidth;
    private int _latestHeight;
    private Windows.Graphics.SizeInt32 _poolSize;
    private long _sequence;
    private long _latestQpc;
    private long _framesArrived;
    private long _firstQpc;
    private readonly LatencyAccumulator _latency = new();
    private int _lostRaised;
    private Timer? _watch;
    private volatile VideoSourceState _state = VideoSourceState.Created;

    private GraphicsCaptureSource(VideoDevice device, VideoSourceId id, string name, IntPtr monitor, IntPtr window, GraphicsCaptureOptions options)
    {
        _device = device;
        Id = id;
        Name = name;
        _monitor = monitor;
        _window = window;
        _options = options;
    }

    public VideoSourceId Id { get; }

    public string Name { get; }

    public VideoSourceState State => _state;

    /// <summary>Whether Windows' capture border could be turned off (Windows 11); false leaves the yellow border on.</summary>
    public bool BorderHidden { get; private set; }

    public VideoCaptureException? Loss { get; private set; }

    public VideoSourceStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new VideoSourceStatistics(_framesArrived, _firstQpc, _latestQpc, _latency.Snapshot(), _latestWidth, _latestHeight);
            }
        }
    }

    public event EventHandler<VideoSourceLostEventArgs>? Lost;

    public event EventHandler<VideoSourceStateChangedEventArgs>? StateChanged;

    /// <summary>Whether this Windows (and this session: not a service) can capture at all.</summary>
    public static bool IsSupported()
    {
        try
        {
            return GraphicsCaptureSession.IsSupported();
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>A display by its GDI device name (<c>\\.\DISPLAY1</c>).</summary>
    public static GraphicsCaptureSource ForDisplay(VideoDevice device, VideoSourceId id, string name, GraphicsCaptureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        var monitor = User32.Monitors().FirstOrDefault(m => string.Equals(m.DeviceName, id.Key, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.DisplayNotFound(name));
        }

        return new GraphicsCaptureSource(device, id, name, monitor.Handle, IntPtr.Zero, options ?? new GraphicsCaptureOptions());
    }

    /// <summary>A top-level window. Windows that Windows hides from capture (display affinity) are refused as protected.</summary>
    public static GraphicsCaptureSource ForWindow(VideoDevice device, VideoSourceId id, string name, GraphicsCaptureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        var hwnd = id.WindowHandle;
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
        {
            throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.WindowNotFound(name));
        }

        if (User32.DisplayAffinity(hwnd) != User32.WdaNone)
        {
            throw new VideoCaptureException(VideoErrorCode.ProtectedContent, VideoErrors.ProtectedWindow(name));
        }

        return new GraphicsCaptureSource(device, id, name, IntPtr.Zero, hwnd, options ?? new GraphicsCaptureOptions());
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupported())
        {
            throw new VideoCaptureException(VideoErrorCode.CaptureUnsupported, VideoErrors.CaptureUnsupported());
        }

        try
        {
            _item = _window != IntPtr.Zero ? WinRtInterop.CreateItemForWindow(_window) : WinRtInterop.CreateItemForMonitor(_monitor);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException)
        {
            throw Classify(ex);
        }

        _item.Closed += OnItemClosed;
        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device.WinRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _pool.FrameArrived += OnFrameArrived;
        try
        {
            _session = _pool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = _options.IncludeCursor;
            BorderHidden = !_options.ShowBorder && WinRtInterop.TrySetBorderRequired(_session, false);
            _session.StartCapture();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            throw Classify(ex);
        }

        _state = VideoSourceState.Running;
        if (_window != IntPtr.Zero)
        {
            // A minimised window delivers no frames and no event; poll so the footer can say why the picture is still.
            _watch = new Timer(_ => WatchWindow(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        return Task.CompletedTask;
    }

    private VideoCaptureException Classify(Exception ex) =>
        ex.HResult == unchecked((int)0x80070005) || ex is UnauthorizedAccessException
            ? new VideoCaptureException(VideoErrorCode.ScreenCapturePrivacyDenied, VideoErrors.ScreenCaptureBlocked(), ex)
            : new VideoCaptureException(VideoErrorCode.SourceNotFound, _window != IntPtr.Zero ? VideoErrors.WindowNotFound(Name) : VideoErrors.DisplayNotFound(Name), ex);

    private void WatchWindow()
    {
        if (_state is VideoSourceState.Lost or VideoSourceState.Stopped)
        {
            return;
        }

        if (!User32.IsWindow(_window))
        {
            RaiseLost(new VideoCaptureException(VideoErrorCode.SourceLost, VideoErrors.WindowClosed(Name)));
            return;
        }

        if (User32.DisplayAffinity(_window) != User32.WdaNone)
        {
            RaiseLost(new VideoCaptureException(VideoErrorCode.ProtectedContent, VideoErrors.ProtectedWindow(Name)));
            return;
        }

        var minimized = User32.IsIconic(_window);
        if (minimized && _state == VideoSourceState.Running)
        {
            _state = VideoSourceState.Paused;
            StateChanged?.Invoke(this, new VideoSourceStateChangedEventArgs(VideoSourceState.Paused, "Window minimised", QpcClock.Now));
        }
        else if (!minimized && _state == VideoSourceState.Paused)
        {
            _state = VideoSourceState.Running;
            StateChanged?.Invoke(this, new VideoSourceStateChangedEventArgs(VideoSourceState.Running, null, QpcClock.Now));
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        var error = _window != IntPtr.Zero
            ? new VideoCaptureException(VideoErrorCode.SourceLost, VideoErrors.WindowClosed(Name))
            : new VideoCaptureException(VideoErrorCode.SourceLost, VideoErrors.DisplayDisconnected(Name));
        RaiseLost(error);
    }

    private void RaiseLost(VideoCaptureException error)
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) != 0)
        {
            return;
        }

        var at = QpcClock.Now;
        Loss = error;
        _state = VideoSourceState.Lost;
        _ = Task.Run(() => Lost?.Invoke(this, new VideoSourceLostEventArgs(Id, error, at)));
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var arrived = QpcClock.Now;
        using var frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        var content = frame.ContentSize;
        var qpc = frame.SystemRelativeTime.Ticks;
        if (content.Width <= 0 || content.Height <= 0)
        {
            return;
        }

        var texturePtr = WinRtInterop.GetTexture(frame.Surface, typeof(ID3D11Texture2D).GUID);
        using (var source = new ID3D11Texture2D(texturePtr))
        {
            var desc = source.Description;
            var width = Math.Min(content.Width, (int)desc.Width) & ~1;
            var height = Math.Min(content.Height, (int)desc.Height) & ~1;
            lock (_gate)
            {
                EnsureLatest(width, height);
                _device.Context.CopySubresourceRegion(_latest!, 0, 0, 0, 0, source, 0, new Vortice.Mathematics.Box(0, 0, 0, width, height, 1));
                _latestQpc = qpc;
                _sequence++;
                _framesArrived++;
                if (_firstQpc == 0)
                {
                    _firstQpc = qpc;
                }

                _latency.Add(arrived - qpc);
            }
        }

        if (content.Width != _poolSize.Width || content.Height != _poolSize.Height)
        {
            // The window was resized (or the display mode changed): new buffers at the new size; the encoder letterboxes.
            _poolSize = content;
            sender.Recreate(_device.WinRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, content);
        }
    }

    private void EnsureLatest(int width, int height)
    {
        if (_latest is not null && _latestWidth == width && _latestHeight == height)
        {
            return;
        }

        if (_latest is not null)
        {
            // The encoder may have submitted work on it; drop it at the next TryGetLatest.
            _retired.Add(_latest);
        }

        _latest = _device.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
        });
        _latestWidth = width;
        _latestHeight = height;
    }

    public bool TryGetLatest(out VideoFrame frame)
    {
        lock (_gate)
        {
            foreach (var texture in _retired)
            {
                texture.Dispose();
            }

            _retired.Clear();
            if (_latest is null)
            {
                frame = default;
                return false;
            }

            frame = new VideoFrame(_latest, 0, _latestWidth, _latestHeight, _latestQpc, _sequence);
            return true;
        }
    }

    public Task StopAsync()
    {
        if (_state is not VideoSourceState.Lost)
        {
            _state = VideoSourceState.Stopped;
        }

        _watch?.Dispose();
        _watch = null;
        if (_pool is not null)
        {
            _pool.FrameArrived -= OnFrameArrived;
        }

        if (_item is not null)
        {
            _item.Closed -= OnItemClosed;
        }

        _session?.Dispose();
        _session = null;
        _pool?.Dispose();
        _pool = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            foreach (var texture in _retired)
            {
                texture.Dispose();
            }

            _retired.Clear();
            _latest?.Dispose();
            _latest = null;
        }
    }
}
