using Memento.Audio;
using Memento.Video.Export;
using Memento.Video.Gpu;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Memento.Video.Sources;

/// <summary>
/// A camera reached through DirectShow (ENGINE-NOTES.md §R): the OBS Virtual Camera and other virtual, software and
/// DirectShow-only cameras that Media Foundation does not list. The feed (<see cref="DirectShowGraph"/>) hands each
/// picture over on DirectShow's streaming thread, which only copies it into a bounded queue (<see cref="RawFrameQueue"/>:
/// three buffers; a picture that finds none free is dropped and counted, so the camera never waits). A conversion
/// thread turns it into NV12 (<see cref="RawFrameConverter"/>) and uploads it into the one texture this source owns, as
/// <see cref="CameraCaptureSource"/> does for Media Foundation cameras, so the pacing and encoding path is the same.
/// Pictures are stamped on the QPC clock when they arrive. A watchdog checks every half second: a virtual camera whose
/// app stopped sending (<see cref="VirtualCameraWatch"/>) or a camera that sent nothing for <see cref="StallAfter"/>
/// is lost; so is a device DirectShow reports removed. Errors map to the §18.6 sentences like a Media Foundation camera's.
/// </summary>
public sealed class DirectShowCameraSource : IVideoSource, ICameraSnapshot
{
    /// <summary>No picture for this long and the camera counts as gone (cameras send at least a few pictures a second).</summary>
    public static readonly TimeSpan DefaultStallAfter = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromMilliseconds(500);

    private readonly VideoDevice? _device;
    private readonly Func<IRawFrameFeed> _open;
    private readonly VirtualCameraWatch? _watch;
    private readonly object _gate = new();
    private readonly LatencyAccumulator _latency = new();
    private readonly RawFrameQueue _queue = new();
    private IRawFrameFeed? _feed;
    private RawVideoFormat? _format;
    private Thread? _converter;
    private Timer? _watchdog;
    private Nv12Frame? _work;
    private Nv12Frame? _shown;
    private ID3D11Texture2D? _latest;
    private int _width;
    private int _height;
    private long _sequence;
    private long _latestQpc;
    private long _firstQpc;
    private long _lastArrival;
    private long _unreadable;
    private int _checking;
    private int _lostRaised;
    private volatile bool _stopping;
    private volatile VideoSourceState _state = VideoSourceState.Created;

    internal DirectShowCameraSource(VideoDevice? device, VideoSourceId id, string name, Func<IRawFrameFeed> open, VirtualCameraWatch? watch, TimeSpan? stallAfter = null)
    {
        _device = device;
        _open = open;
        _watch = watch;
        Id = id;
        Name = name;
        StallAfter = stallAfter ?? DefaultStallAfter;
    }

    public VideoSourceId Id { get; }

    public string Name { get; }

    public VideoSourceState State => _state;

    public VideoCaptureException? Loss { get; private set; }

    public TimeSpan StallAfter { get; }

    /// <summary>The format the graph delivers (null before start).</summary>
    public RawVideoFormat? Format => _format;

    /// <summary>Pictures dropped at the hand-over because the conversion fell behind.</summary>
    public long FramesDropped => _queue.Dropped;

    /// <summary>Pictures that could not be read (a buffer shorter than its format says).</summary>
    public long FramesUnreadable => Interlocked.Read(ref _unreadable);

    public VideoSourceStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new VideoSourceStatistics(_sequence, _firstQpc, _latestQpc, _latency.Snapshot(), _width, _height);
            }
        }
    }

    public event EventHandler<VideoSourceLostEventArgs>? Lost;

    public event EventHandler<VideoSourceStateChangedEventArgs>? StateChanged;

    public static DirectShowCameraSource ForCamera(VideoDevice device, VideoSourceId id, string name)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (id.Backend != CameraBackend.DirectShow)
        {
            throw new ArgumentException("Not a DirectShow camera id.", nameof(id));
        }

        return new DirectShowCameraSource(device, id, name, () => DirectShowGraph.Open(id.Key, name), VirtualCameraWatch.For(id));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DirectShowGraph.IsHardware(Id.Key))
        {
            // A real camera reached through DirectShow is still behind Windows' camera switches; a virtual one is not.
            var access = CameraPrivacy.Read();
            if (access is CameraAccess.DeniedForPc or CameraAccess.DeniedForUser or CameraAccess.DeniedForDesktopApps)
            {
                throw CameraErrors.Classify(CameraErrors.AccessDenied, Name, access, null);
            }
        }

        if (_watch is not null && !_watch.IsSending())
        {
            throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.VirtualCameraNotStarted(Name, _watch.App)) { Detail = _watch.App };
        }

        var feed = _open();
        try
        {
            _format = feed.Format;
            (_width, _height) = _format.EvenSize;
            _work = new Nv12Frame(_width, _height);
            _shown = new Nv12Frame(_width, _height);
            _shown.FillBlack();
            _feed = feed;
            _state = VideoSourceState.Running;
            Volatile.Write(ref _lastArrival, QpcClock.Now);
            _converter = new Thread(ConvertLoop) { Name = "Memento camera (DirectShow)", IsBackground = true, Priority = ThreadPriority.AboveNormal };
            _converter.Start();
            feed.Start(OnFrame, OnError);
        }
        catch
        {
            _stopping = true;
            _queue.Complete();
            _converter?.Join(TimeSpan.FromSeconds(2));
            _feed = null;
            feed.Dispose();
            _state = VideoSourceState.Stopped;
            throw;
        }

        _watchdog = new Timer(_ => Check(), null, WatchdogPeriod, WatchdogPeriod);
        return Task.CompletedTask;
    }

    private unsafe void OnFrame(IntPtr data, int length)
    {
        var arrived = QpcClock.Now;
        Volatile.Write(ref _lastArrival, arrived);
        _queue.TryWrite(new ReadOnlySpan<byte>((void*)data, length), arrived);
    }

    private void OnError(int hresult) => RaiseLost(Classify(hresult));

    private void ConvertLoop()
    {
        while (!_stopping)
        {
            var frame = _queue.Take(TimeSpan.FromMilliseconds(250));
            if (frame is null)
            {
                continue;
            }

            bool converted;
            try
            {
                converted = RawFrameConverter.ToNv12(_format!, frame.Buffer.AsSpan(0, frame.Length), _work!);
            }
            finally
            {
                _queue.Return(frame);
            }

            if (!converted)
            {
                Interlocked.Increment(ref _unreadable);
                continue;
            }

            Publish(frame.Qpc);
        }
    }

    private unsafe void Publish(long qpc)
    {
        lock (_gate)
        {
            if (_device is not null)
            {
                _latest ??= _device.Device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)_width,
                    Height = (uint)_height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Vortice.DXGI.Format.NV12,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget, // The video processor makes input views only of render-target textures.
                });
                fixed (byte* pixels = _work!.Data)
                {
                    // NV12 in one subresource: the chroma rows follow the luma rows at the same pitch.
                    _device.Context.UpdateSubresource(_latest, 0, null, (IntPtr)pixels, (uint)_width, 0);
                }
            }

            (_shown, _work) = (_work, _shown);
            _latestQpc = qpc;
            _sequence++;
            if (_firstQpc == 0)
            {
                _firstQpc = qpc;
            }

            _latency.Add(QpcClock.Now - qpc);
        }
    }

    /// <summary>The watchdog: the virtual camera's app, then the time since the last picture.</summary>
    private void Check()
    {
        if (_stopping || _state != VideoSourceState.Running || Interlocked.Exchange(ref _checking, 1) != 0)
        {
            return;
        }

        try
        {
            if (_watch is not null && !_watch.IsSending())
            {
                var app = _watch.App;
                RaiseLost(new VideoCaptureException(VideoErrorCode.SourceLost, VideoErrors.VirtualCameraStopped(Name, app))
                {
                    Detail = app,
                    AtPosition = atMs => VideoErrors.VirtualCameraStopped(Name, app, atMs),
                });
            }
            else if (QpcClock.Now - Volatile.Read(ref _lastArrival) > (long)(StallAfter.TotalMilliseconds * QpcClock.TicksPerMillisecond))
            {
                RaiseLost(Classify(CameraErrors.DeviceInvalidated));
            }
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    private VideoCaptureException Classify(int hresult)
    {
        var hardware = DirectShowGraph.IsHardware(Id.Key);
        return CameraErrors.Classify(
            hresult,
            Name,
            hardware ? CameraPrivacy.Read() : CameraAccess.Unknown,
            hardware ? CameraUsage.AppUsingCamera(Environment.ProcessPath) : null);
    }

    private void RaiseLost(VideoCaptureException error)
    {
        if (_stopping || Interlocked.Exchange(ref _lostRaised, 1) != 0)
        {
            return;
        }

        var at = QpcClock.Now;
        Loss = error;
        _state = VideoSourceState.Lost;
        _ = Task.Run(() => Lost?.Invoke(this, new VideoSourceLostEventArgs(Id, error, at)));
        if (error.Code == VideoErrorCode.CameraInUse)
        {
            _ = Task.Run(() => StateChanged?.Invoke(this, new VideoSourceStateChangedEventArgs(VideoSourceState.Lost, error.Message, at)));
        }
    }

    public bool TryGetLatest(out VideoFrame frame)
    {
        lock (_gate)
        {
            if (_sequence == 0)
            {
                frame = default;
                return false;
            }

            frame = new VideoFrame(_latest, 0, _width, _height, _latestQpc, _sequence);
            return true;
        }
    }

    Nv12Frame? ICameraSnapshot.Snapshot() => Snapshot();

    internal Nv12Frame? Snapshot()
    {
        lock (_gate)
        {
            if (_sequence == 0 || _shown is null)
            {
                return null;
            }

            var copy = new Nv12Frame(_width, _height);
            _shown.Data.CopyTo(copy.Data, 0);
            return copy;
        }
    }

    public Task StopAsync()
    {
        _stopping = true;
        if (_state is not VideoSourceState.Lost)
        {
            _state = VideoSourceState.Stopped;
        }

        _watchdog?.Dispose();
        _watchdog = null;
        _feed?.Stop();
        _queue.Complete();
        _converter?.Join(TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _feed?.Dispose();
        _feed = null;
        _queue.Dispose();
        lock (_gate)
        {
            _latest?.Dispose();
            _latest = null;
        }
    }
}
