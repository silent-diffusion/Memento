using Memento.Audio;
using Memento.Video.Gpu;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;

namespace Memento.Video.Sources;

/// <summary>
/// A camera through the Media Foundation source reader (ENGINE-NOTES.md §M.2), on the shared device so frames stay on
/// the GPU: the reader's advanced video processing decodes MJPG and converts to NV12, and each frame is copied into
/// one texture this source owns. A dedicated thread blocks in <c>ReadSample</c>, so the camera never waits for the
/// encoder. Frames are stamped on the QPC clock: the reader's sample time plus the offset measured at the first frame.
/// Privacy denial, another app holding the camera and unplugging map to the §18.6 errors (<see cref="CameraErrors"/>).
/// </summary>
public sealed class CameraCaptureSource : IVideoSource, ICameraSnapshot
{
    private readonly VideoDevice _device;
    private readonly object _gate = new();
    private readonly LatencyAccumulator _latency = new();
    private IMFMediaSource? _mediaSource;
    private IMFSourceReader? _reader;
    private Thread? _thread;
    private volatile bool _stopping;
    private ID3D11Texture2D? _latest;
    private ID3D11Texture2D? _staging;
    private int _width;
    private int _height;
    private long _sequence;
    private long _latestQpc;
    private long _firstQpc;
    private long? _clockOffset;
    private int _lostRaised;
    private volatile VideoSourceState _state = VideoSourceState.Created;

    private CameraCaptureSource(VideoDevice device, VideoSourceId id, string name)
    {
        _device = device;
        Id = id;
        Name = name;
    }

    public VideoSourceId Id { get; }

    public string Name { get; }

    public VideoSourceState State => _state;

    public VideoCaptureException? Loss { get; private set; }

    /// <summary>The capture format chosen (null before start).</summary>
    public CameraFormatChooser.Format? Format { get; private set; }

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

    public static CameraCaptureSource ForCamera(VideoDevice device, VideoSourceId id, string name)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (id.Kind != VideoSourceKind.Camera)
        {
            throw new ArgumentException("Not a camera id.", nameof(id));
        }

        return new CameraCaptureSource(device, id, name);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var access = CameraPrivacy.Read();
        if (access is CameraAccess.DeniedForPc or CameraAccess.DeniedForUser or CameraAccess.DeniedForDesktopApps)
        {
            throw CameraErrors.Classify(CameraErrors.AccessDenied, Name, access, null);
        }

        MediaFoundationRuntime.EnsureStarted();
        try
        {
            using (var attributes = MediaFactory.MFCreateAttributes(2))
            {
                attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
                attributes.Set(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink, Id.Key);
                _mediaSource = MediaFactory.MFCreateDeviceSource(attributes);
            }

            using (var attributes = MediaFactory.MFCreateAttributes(3))
            {
                attributes.Set(SourceReaderAttributeKeys.D3DManager, _device.DeviceManager);
                attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u);
                attributes.Set(SourceReaderAttributeKeys.DisconnectMediasourceOnShutdown, 1u);
                _reader = MediaFactory.MFCreateSourceReaderFromMediaSource(_mediaSource, attributes);
            }

            Format = ChooseFormat(_reader);
            if (Format is null)
            {
                throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.CameraNotFound(Name));
            }

            using (var native = _reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, Format.Index))
            {
                _reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, native);
            }

            using (var output = MediaFactory.MFCreateMediaType())
            {
                output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
                output.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)Format.Width, (uint)Format.Height));
                _reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, output);
            }

            _width = Format.Width & ~1;
            _height = Format.Height & ~1;
        }
        catch (SharpGenException ex)
        {
            Release();
            throw CameraErrors.Classify(ex.ResultCode.Code, Name, CameraPrivacy.Read(), CameraUsage.AppUsingCamera(Environment.ProcessPath), inner: ex);
        }

        _state = VideoSourceState.Running;
        _thread = new Thread(ReadLoop) { Name = "Memento camera", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        return Task.CompletedTask;
    }

    private static CameraFormatChooser.Format? ChooseFormat(IMFSourceReader reader)
    {
        var formats = new List<CameraFormatChooser.Format>();
        for (var i = 0; i < 256; i++)
        {
            IMFMediaType type;
            try
            {
                type = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, i);
            }
            catch (SharpGenException)
            {
                break;
            }

            using (type)
            {
                MediaFactory.UnpackSize(type.GetUInt64(MediaTypeAttributeKeys.FrameSize), out var width, out var height);
                MediaFactory.UnpackRatio(type.GetUInt64(MediaTypeAttributeKeys.FrameRate), out var numerator, out var denominator);
                var subtype = type.GetGUID(MediaTypeAttributeKeys.Subtype);
                var compressed = subtype != VideoFormatGuids.NV12 && subtype != VideoFormatGuids.YUY2;
                formats.Add(new CameraFormatChooser.Format(i, (int)width, (int)height, denominator == 0 ? 0 : numerator / (double)denominator, compressed));
            }
        }

        return CameraFormatChooser.Choose(formats);
    }

    private void ReadLoop()
    {
        while (!_stopping)
        {
            IMFSample? sample = null;
            try
            {
                sample = _reader!.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out var time);
                if ((flags & SourceReaderFlag.Error) != 0 || (flags & SourceReaderFlag.EndOfStream) != 0)
                {
                    RaiseLost(CameraErrors.DeviceInvalidated);
                    return;
                }

                if (sample is not null)
                {
                    Store(sample, time);
                }
            }
            catch (SharpGenException ex)
            {
                if (!_stopping)
                {
                    RaiseLost(ex.ResultCode.Code, ex);
                }

                return;
            }
            finally
            {
                sample?.Dispose();
            }
        }
    }

    private void Store(IMFSample sample, long time)
    {
        var arrived = QpcClock.Now;
        _clockOffset ??= arrived - time;
        var qpc = time + _clockOffset.Value;
        using var buffer = sample.GetBufferByIndex(0);
        using var dxgi = buffer.QueryInterfaceOrNull<IMFDXGIBuffer>();
        lock (_gate)
        {
            EnsureLatest();
            if (dxgi is not null)
            {
                using var texture = new ID3D11Texture2D(dxgi.GetResource(typeof(ID3D11Texture2D).GUID));
                _device.Context.CopySubresourceRegion(_latest!, 0, 0, 0, 0, texture, dxgi.SubresourceIndex, new Vortice.Mathematics.Box(0, 0, 0, _width, _height, 1));
            }
            else
            {
                CopyFromMemory(buffer);
            }

            _latestQpc = qpc;
            _sequence++;
            if (_firstQpc == 0)
            {
                _firstQpc = qpc;
            }

            _latency.Add(arrived - qpc);
        }
    }

    private unsafe void CopyFromMemory(IMFMediaBuffer buffer)
    {
        using var buffer2d = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
        if (buffer2d is null)
        {
            return;
        }

        buffer2d.Lock2D(out var scanline, out var pitch);
        try
        {
            // NV12: one subresource holds both planes; UpdateSubresource takes the luma pitch for the whole surface.
            _device.Context.UpdateSubresource(_latest!, 0, null, scanline, (uint)pitch, 0);
        }
        finally
        {
            buffer2d.Unlock2D();
        }
    }

    private void EnsureLatest()
    {
        if (_latest is not null)
        {
            return;
        }

        _latest = _device.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Vortice.DXGI.Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget, // The video processor makes input views only of render-target textures (§R).
        });
    }

    private void RaiseLost(int hresult, Exception? inner = null)
    {
        if (Interlocked.Exchange(ref _lostRaised, 1) != 0)
        {
            return;
        }

        var at = QpcClock.Now;
        var error = CameraErrors.Classify(hresult, Name, CameraPrivacy.Read(), CameraUsage.AppUsingCamera(Environment.ProcessPath), inner: inner);
        Loss = error;
        _state = VideoSourceState.Lost;
        _ = Task.Run(() => Lost?.Invoke(this, new VideoSourceLostEventArgs(Id, error, at)));
        if (error.Code == VideoErrorCode.CameraInUse)
        {
            _ = Task.Run(() => StateChanged?.Invoke(this, new VideoSourceStateChangedEventArgs(VideoSourceState.Lost, error.Message, at)));
        }
    }

    Export.Nv12Frame? ICameraSnapshot.Snapshot() => Snapshot();

    /// <summary>
    /// A copy of the latest picture in managed memory (the Recording screen's thumbnail of a camera that is being
    /// recorded, which cannot be opened a second time); <c>null</c> before the first picture.
    /// </summary>
    internal Export.Nv12Frame? Snapshot()
    {
        lock (_gate)
        {
            if (_latest is null || _sequence == 0)
            {
                return null;
            }

            _staging ??= _device.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_width,
                Height = (uint)_height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Vortice.DXGI.Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
            });
            _device.Context.CopyResource(_staging, _latest);
            var mapped = _device.Context.Map(_staging, 0, MapMode.Read);
            try
            {
                var frame = new Export.Nv12Frame(_width, _height);
                frame.CopyFrom(mapped.DataPointer, (int)mapped.RowPitch, _width, _height);
                return frame;
            }
            finally
            {
                _device.Context.Unmap(_staging, 0);
            }
        }
    }

    public bool TryGetLatest(out VideoFrame frame)
    {
        lock (_gate)
        {
            if (_latest is null || _sequence == 0)
            {
                frame = default;
                return false;
            }

            frame = new VideoFrame(_latest, 0, _width, _height, _latestQpc, _sequence);
            return true;
        }
    }

    public Task StopAsync()
    {
        _stopping = true;
        if (_state is not VideoSourceState.Lost)
        {
            _state = VideoSourceState.Stopped;
        }

        try
        {
            _mediaSource?.Shutdown();
        }
        catch (SharpGenException)
        {
            // Already gone (unplugged).
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    private void Release()
    {
        _staging?.Dispose();
        _staging = null;
        _reader?.Dispose();
        _reader = null;
        _mediaSource?.Dispose();
        _mediaSource = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Release();
        lock (_gate)
        {
            _latest?.Dispose();
            _latest = null;
        }
    }
}
