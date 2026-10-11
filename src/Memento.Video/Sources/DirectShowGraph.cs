using System.Runtime.InteropServices;
using Memento.Video.Interop;

namespace Memento.Video.Sources;

/// <summary>
/// A DirectShow camera as a feed of pictures in system memory (ENGINE-NOTES.md §R): the device's capture filter, the
/// capture pin set to the chosen format (<see cref="CameraFormatChooser"/> over <c>IAMStreamConfig</c>'s list), qedit's
/// sample grabber asking for a layout <see cref="RawFrameConverter"/> reads (the camera's own when it can, else NV12,
/// YUY2, RGB32 or RGB24, with DirectShow's decoder inserted for MJPG), and the null renderer. The graph runs without a
/// reference clock, so pictures are handed over the moment the camera delivers them. A thread waits on the graph's
/// events for the device going away (<c>EC_DEVICE_LOST</c>, <c>EC_ERRORABORT</c>, <c>EC_COMPLETE</c>).
/// </summary>
internal sealed unsafe class DirectShowGraph : IRawFrameFeed
{
    private readonly string _name;
    private readonly bool _hardware;
    private DirectShow.IGraphBuilder? _graph;
    private DirectShow.ICaptureGraphBuilder2? _builder;
    private DirectShow.IBaseFilter? _source;
    private DirectShow.IBaseFilter? _grabber;
    private DirectShow.IBaseFilter? _renderer;
    private SampleGrabberCallback? _callback;
    private RawFrameHandler? _onFrame;
    private Action<int>? _onError;
    private Thread? _events;
    private volatile bool _stopping;
    private int _errorRaised;

    private DirectShowGraph(string name, bool hardware, RawVideoFormat format, RawVideoFormat? native)
    {
        _name = name;
        _hardware = hardware;
        Format = format;
        Native = native;
    }

    public RawVideoFormat Format { get; }

    /// <summary>The capture pin's own format when one was chosen from its list (MJPG when DirectShow decodes it).</summary>
    public RawVideoFormat? Native { get; }

    /// <summary>
    /// Builds the graph for the device with id key <paramref name="key"/> (<c>dshow:…</c>). Throws
    /// <see cref="VideoCaptureException"/>: not connected, in use, or a format Memento can't read.
    /// </summary>
    public static DirectShowGraph Open(string key, string name, int maxWidth = 1920, int maxHeight = 1080, double minFrameRate = 24) =>
        ComApartment.InMta(() => Build(key, name, maxWidth, maxHeight, minFrameRate));

    /// <summary>Whether the id key names a device with a device path (a real camera, which the privacy switches cover).</summary>
    public static bool IsHardware(string key) => key.StartsWith(CameraListMerger.DirectShowPrefix + @"\\?\", StringComparison.Ordinal);

    private static DirectShowGraph Build(string key, string name, int maxWidth, int maxHeight, double minFrameRate)
    {
        var hardware = IsHardware(key);
        DirectShow.IBaseFilter? source;
        try
        {
            source = DirectShowDevices.Bind(key);
        }
        catch (COMException ex)
        {
            throw Classify(ex.HResult, name, hardware, ex);
        }

        if (source is null)
        {
            throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.CameraNotFound(name));
        }

        var keepSource = false;
        try
        {
            var native = ChooseNative(source, maxWidth, maxHeight, minFrameRate);
            var candidates = new List<Guid>();
            if (native is { } chosen && DirectShowFormats.PixelOf(chosen.Subtype) != RawPixelFormat.Unsupported)
            {
                candidates.Add(chosen.Subtype);
            }

            candidates.AddRange(DirectShowFormats.Fallbacks.Where(f => !candidates.Contains(f)));
            var lastError = DirectShow.CannotConnect;
            foreach (var subtype in candidates)
            {
                var graph = TryConnect(source, name, hardware, native, subtype, out var hr);
                if (graph is not null)
                {
                    keepSource = true;
                    return graph;
                }

                lastError = hr;
            }

            throw lastError is DirectShow.CannotConnect or DirectShow.NoAcceptableTypes or DirectShow.TypeNotAccepted or DirectShow.NotFound
                ? new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.CameraFormatRefused(name, native?.Format.SubtypeName))
                : Classify(lastError, name, hardware, Marshal.GetExceptionForHR(lastError));
        }
        finally
        {
            if (!keepSource)
            {
                DirectShow.Release(source);
            }
        }
    }

    /// <summary>The capture pin's formats, the one <see cref="CameraFormatChooser"/> picks set on the pin; null when the pin has no list.</summary>
    private static (int Index, Guid Subtype, RawVideoFormat Format)? ChooseNative(DirectShow.IBaseFilter source, int maxWidth, int maxHeight, double minFrameRate)
    {
        var config = StreamConfig(source);
        if (config is null)
        {
            return null;
        }

        var caps = Marshal.AllocCoTaskMem(256);
        try
        {
            if (config.GetNumberOfCapabilities(out var count, out _) < 0)
            {
                return null;
            }

            var formats = new List<(int Index, Guid Subtype, RawVideoFormat Format)>();
            for (var i = 0; i < Math.Min(count, 512); i++)
            {
                if (config.GetStreamCaps(i, out var pointer, caps) < 0 || pointer == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var type = Marshal.PtrToStructure<DirectShow.AmMediaType>(pointer);
                    var format = Read(type);
                    if (type.MajorType == DirectShow.MediaTypeVideo && format is not null
                        && (format.Pixel != RawPixelFormat.Unsupported || type.SubType == DirectShowFormats.Mjpg))
                    {
                        formats.Add((i, type.SubType, format));
                    }
                }
                finally
                {
                    DirectShow.DeleteMediaType(pointer);
                }
            }

            var choice = CameraFormatChooser.Choose(
                formats.Select(f => new CameraFormatChooser.Format(f.Index, f.Format.Width, f.Format.Height, f.Format.FrameRate, f.Format.Pixel == RawPixelFormat.Unsupported)).ToList(),
                maxWidth,
                maxHeight,
                minFrameRate);
            if (choice is null)
            {
                return null;
            }

            var picked = formats.First(f => f.Index == choice.Index);
            if (config.GetStreamCaps(picked.Index, out var chosen, caps) >= 0 && chosen != IntPtr.Zero)
            {
                try
                {
                    // A filter that refuses its own listed format keeps its default; the grabber still reads what comes.
                    _ = config.SetFormat(chosen);
                }
                finally
                {
                    DirectShow.DeleteMediaType(chosen);
                }
            }

            return picked;
        }
        finally
        {
            Marshal.FreeCoTaskMem(caps);
            // FindInterface looks at the filter before its pins: a filter that is its own IAMStreamConfig (the OBS Virtual
            // Camera's) hands back the source's own RCW, which must stay alive.
            if (!ReferenceEquals(config, source))
            {
                DirectShow.Release(config);
            }
        }
    }

    private static DirectShow.IAMStreamConfig? StreamConfig(DirectShow.IBaseFilter source)
    {
        // FindInterface needs the filter in a graph with the builder attached.
        var graph = DirectShow.Create<DirectShow.IGraphBuilder>(DirectShow.ClsidFilterGraph, typeof(DirectShow.IGraphBuilder).GUID);
        var builder = DirectShow.Create<DirectShow.ICaptureGraphBuilder2>(DirectShow.ClsidCaptureGraphBuilder2, typeof(DirectShow.ICaptureGraphBuilder2).GUID);
        try
        {
            Marshal.ThrowExceptionForHR(builder.SetFiltergraph(graph));
            Marshal.ThrowExceptionForHR(graph.AddFilter(source, "Camera"));
            try
            {
                var category = DirectShow.PinCategoryCapture;
                var video = DirectShow.MediaTypeVideo;
                var hr = builder.FindInterface(&category, &video, source, DirectShow.IidStreamConfig, out var found);
                if (hr < 0 || found is null)
                {
                    hr = builder.FindInterface(null, &video, source, DirectShow.IidStreamConfig, out found);
                }

                return hr >= 0 ? found as DirectShow.IAMStreamConfig : null;
            }
            finally
            {
                graph.RemoveFilter(source);
            }
        }
        finally
        {
            DirectShow.Release(builder);
            DirectShow.Release(graph);
        }
    }

    /// <summary>A graph from the source through a sample grabber asking for <paramref name="subtype"/>; null (with the HRESULT) when it does not connect.</summary>
    private static DirectShowGraph? TryConnect(DirectShow.IBaseFilter source, string name, bool hardware, (int Index, Guid Subtype, RawVideoFormat Format)? native, Guid subtype, out int hr)
    {
        var graph = DirectShow.Create<DirectShow.IGraphBuilder>(DirectShow.ClsidFilterGraph, typeof(DirectShow.IGraphBuilder).GUID);
        var builder = DirectShow.Create<DirectShow.ICaptureGraphBuilder2>(DirectShow.ClsidCaptureGraphBuilder2, typeof(DirectShow.ICaptureGraphBuilder2).GUID);
        var grabber = DirectShow.Create<DirectShow.IBaseFilter>(DirectShow.ClsidSampleGrabber, DirectShow.IidBaseFilter);
        var renderer = DirectShow.Create<DirectShow.IBaseFilter>(DirectShow.ClsidNullRenderer, DirectShow.IidBaseFilter);
        DirectShowGraph? result = null;
        try
        {
            hr = builder.SetFiltergraph(graph);
            if (hr >= 0)
            {
                hr = graph.AddFilter(source, "Camera");
            }

            if (hr < 0)
            {
                return null;
            }

            var sampleGrabber = (DirectShow.ISampleGrabber)grabber;
            var wanted = new DirectShow.AmMediaType { MajorType = DirectShow.MediaTypeVideo, SubType = subtype };
            Marshal.ThrowExceptionForHR(sampleGrabber.SetMediaType(in wanted));
            Marshal.ThrowExceptionForHR(graph.AddFilter(grabber, "Memento grabber"));
            Marshal.ThrowExceptionForHR(graph.AddFilter(renderer, "Memento null renderer"));

            // The camera's first output pin straight to the grabber (DirectShow inserts a decoder when one is needed), then
            // to the null renderer: the OBS Virtual Camera then starts in 140–160 ms, where the capture graph builder's RenderStream
            // took about a second more. RenderStream, by pin category, is the fallback for filters whose first pin will not do.
            hr = ConnectPins(graph, source, grabber, renderer);
            if (hr < 0)
            {
                DisconnectAll(graph, source, grabber, renderer);
                var category = DirectShow.PinCategoryCapture;
                var video = DirectShow.MediaTypeVideo;
                hr = builder.RenderStream(&category, &video, source, grabber, renderer);
                if (hr < 0)
                {
                    hr = builder.RenderStream(null, &video, source, grabber, renderer);
                }
            }

            if (hr < 0)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(sampleGrabber.GetConnectedMediaType(out var connected));
            RawVideoFormat? format;
            try
            {
                format = Read(connected);
            }
            finally
            {
                DirectShow.FreeMediaType(ref connected);
            }

            if (format is null || format.Pixel == RawPixelFormat.Unsupported || format.EvenSize.Width <= 0 || format.EvenSize.Height <= 0)
            {
                hr = DirectShow.TypeNotAccepted;
                return null;
            }

            result = new DirectShowGraph(name, hardware, format, native?.Format)
            {
                _graph = graph,
                _builder = builder,
                _source = source,
                _grabber = grabber,
                _renderer = renderer,
            };
            result._callback = new SampleGrabberCallback(result.Deliver);
            Marshal.ThrowExceptionForHR(sampleGrabber.SetBufferSamples(false));
            Marshal.ThrowExceptionForHR(sampleGrabber.SetOneShot(false));
            Marshal.ThrowExceptionForHR(sampleGrabber.SetCallback(result._callback, 1));

            // No reference clock: a live source's pictures go straight through instead of waiting for their presentation time.
            if (graph is DirectShow.IMediaFilter filter)
            {
                _ = filter.SetSyncSource(IntPtr.Zero);
            }

            return result;
        }
        catch (COMException ex)
        {
            hr = ex.HResult;
            result = null;
            return null;
        }
        finally
        {
            if (result is null)
            {
                _ = graph.RemoveFilter(source);
                DirectShow.Release(renderer);
                DirectShow.Release(grabber);
                DirectShow.Release(builder);
                DirectShow.Release(graph);
            }
        }
    }

    private const int PinInput = 0;
    private const int PinOutput = 1;

    /// <summary>Source → grabber → null renderer through each filter's first pin of the right direction.</summary>
    private static int ConnectPins(DirectShow.IGraphBuilder graph, DirectShow.IBaseFilter source, DirectShow.IBaseFilter grabber, DirectShow.IBaseFilter renderer)
    {
        var pins = new[] { FirstPin(source, PinOutput), FirstPin(grabber, PinInput), FirstPin(grabber, PinOutput), FirstPin(renderer, PinInput) };
        try
        {
            if (pins.Any(p => p == IntPtr.Zero))
            {
                return DirectShow.NotFound;
            }

            var hr = graph.Connect(pins[0], pins[1]);
            return hr < 0 ? hr : graph.Connect(pins[2], pins[3]);
        }
        finally
        {
            foreach (var pin in pins.Where(p => p != IntPtr.Zero))
            {
                Marshal.Release(pin);
            }
        }
    }

    /// <summary>Undoes a half-made connection: the grabber and renderer leave and rejoin the graph, and the camera's pin is freed.</summary>
    private static void DisconnectAll(DirectShow.IGraphBuilder graph, DirectShow.IBaseFilter source, DirectShow.IBaseFilter grabber, DirectShow.IBaseFilter renderer)
    {
        _ = graph.RemoveFilter(grabber);
        _ = graph.RemoveFilter(renderer);
        var output = FirstPin(source, PinOutput);
        if (output != IntPtr.Zero)
        {
            var pin = (DirectShow.IPin)Marshal.GetUniqueObjectForIUnknown(output);
            try
            {
                if (pin.ConnectedTo(out var peer) >= 0 && peer != IntPtr.Zero)
                {
                    _ = graph.Disconnect(peer);
                    Marshal.Release(peer);
                }

                _ = graph.Disconnect(output);
            }
            finally
            {
                Marshal.FinalReleaseComObject(pin);
                Marshal.Release(output);
            }
        }

        _ = graph.AddFilter(grabber, "Memento grabber");
        _ = graph.AddFilter(renderer, "Memento null renderer");
    }

    /// <summary>The filter's first pin of <paramref name="direction"/>, AddRef'd (the caller releases it), or zero.</summary>
    private static IntPtr FirstPin(DirectShow.IBaseFilter filter, int direction)
    {
        if (filter.EnumPins(out var enumPointer) < 0 || enumPointer == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var pins = (DirectShow.IEnumPins)Marshal.GetUniqueObjectForIUnknown(enumPointer);
        Marshal.Release(enumPointer);
        try
        {
            var one = new IntPtr[1];
            while (pins.Next(1, one, IntPtr.Zero) == DirectShow.SOk && one[0] != IntPtr.Zero)
            {
                var pin = (DirectShow.IPin)Marshal.GetUniqueObjectForIUnknown(one[0]);
                int found;
                try
                {
                    found = pin.QueryDirection(out var pinDirection) >= 0 ? pinDirection : -1;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(pin);
                }

                if (found == direction)
                {
                    return one[0];
                }

                Marshal.Release(one[0]);
            }

            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FinalReleaseComObject(pins);
        }
    }

    private static RawVideoFormat? Read(DirectShow.AmMediaType type)
    {
        if (type.FormatPointer == IntPtr.Zero || type.FormatSize <= 0)
        {
            return null;
        }

        var block = new ReadOnlySpan<byte>((void*)type.FormatPointer, type.FormatSize);
        return DirectShowFormats.Parse(type.SubType, type.FormatType, block);
    }

    public void Start(RawFrameHandler onFrame, Action<int> onError)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ArgumentNullException.ThrowIfNull(onError);
        _onFrame = onFrame;
        _onError = onError;
        ComApartment.InMta(() =>
        {
            var control = (DirectShow.IMediaControl)_graph!;
            var hr = control.Run();
            if (hr == DirectShow.SFalse)
            {
                hr = control.GetState(3000, out _);
            }

            if (hr < 0)
            {
                _ = control.Stop();
                throw Classify(hr, _name, _hardware, Marshal.GetExceptionForHR(hr));
            }

            return 0;
        });
        _events = new Thread(WatchEvents) { Name = "Memento DirectShow events", IsBackground = true };
        _events.Start();
    }

    private void Deliver(IntPtr data, int length) => _onFrame?.Invoke(data, length);

    private void WatchEvents()
    {
        var events = (DirectShow.IMediaEvent)_graph!;
        while (!_stopping)
        {
            if (events.GetEvent(out var code, out var first, out var second, 250) != DirectShow.SOk)
            {
                continue;
            }

            try
            {
                var failure = code switch
                {
                    DirectShow.EcDeviceLost when second == IntPtr.Zero => CameraErrors.DeviceRemoved,
                    DirectShow.EcErrorAbort or DirectShow.EcStreamErrorStopped => first.ToInt32() < 0 ? first.ToInt32() : CameraErrors.DeviceInvalidated,
                    DirectShow.EcComplete => CameraErrors.DeviceInvalidated,
                    _ => 0,
                };
                if (failure != 0 && !_stopping && Interlocked.Exchange(ref _errorRaised, 1) == 0)
                {
                    _onError?.Invoke(failure);
                }
            }
            finally
            {
                _ = events.FreeEventParams(code, first, second);
            }
        }
    }

    public void Stop()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _callback?.Disable();
        ComApartment.InMta(() =>
        {
            if (_graph is DirectShow.IMediaControl control)
            {
                try
                {
                    _ = control.Stop();
                }
                catch (COMException)
                {
                    // The device is gone already.
                }
            }

            return 0;
        });
        _events?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();

        // The objects were made in the MTA; a filter registered as apartment-threaded can't be reached from an STA.
        ComApartment.InMta(() =>
        {
            if (_grabber is DirectShow.ISampleGrabber grabber)
            {
                _ = grabber.SetCallback(null, 1);
            }

            if (_graph is not null && _source is not null)
            {
                _ = _graph.RemoveFilter(_source);
            }

            DirectShow.Release(_renderer);
            DirectShow.Release(_grabber);
            DirectShow.Release(_source);
            DirectShow.Release(_builder);
            DirectShow.Release(_graph);
            return 0;
        });
        _renderer = _grabber = _source = null;
        _builder = null;
        _graph = null;
    }

    private static VideoCaptureException Classify(int hresult, string name, bool hardware, Exception? inner) =>
        CameraErrors.Classify(
            hresult,
            name,
            hardware ? CameraPrivacy.Read() : CameraAccess.Unknown,
            hardware ? CameraUsage.AppUsingCamera(Environment.ProcessPath) : null,
            inner: inner);
}
