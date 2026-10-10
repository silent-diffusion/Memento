using Memento.Video.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Windows.Graphics.DirectX.Direct3D11;

namespace Memento.Video.Gpu;

/// <summary>
/// One Direct3D 11 device shared by capture, conversion and the hardware encoder: the WinRT wrapper for the capture
/// frame pool and the Media Foundation device manager for the encoder. Multithread-protected, because frames arrive on
/// the capture thread pool and are converted on the encoder thread.
/// </summary>
public sealed class VideoDevice : IDisposable
{
    private bool _disposed;

    private VideoDevice(ID3D11Device device, IDirect3DDevice winRtDevice, IMFDXGIDeviceManager manager, GpuAdapterInfo adapter)
    {
        Device = device;
        Context = device.ImmediateContext;
        WinRtDevice = winRtDevice;
        DeviceManager = manager;
        Adapter = adapter;
        VideoDevice1 = device.QueryInterface<ID3D11VideoDevice>();
        VideoContext = Context.QueryInterface<ID3D11VideoContext>();
    }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    internal ID3D11VideoDevice VideoDevice1 { get; }

    internal ID3D11VideoContext VideoContext { get; }

    public IDirect3DDevice WinRtDevice { get; }

    /// <summary>The device manager the sink writer and the camera reader share (<c>MF_SINK_WRITER_D3D_MANAGER</c>).</summary>
    public IMFDXGIDeviceManager DeviceManager { get; }

    public GpuAdapterInfo Adapter { get; }

    /// <summary>Lists the hardware adapters (no software rasterizer).</summary>
    public static IReadOnlyList<GpuAdapterInfo> ListAdapters()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var result = new List<GpuAdapterInfo>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            using (adapter)
            {
                var d = adapter.Description1;
                if ((d.Flags & AdapterFlags.Software) != 0)
                {
                    continue;
                }

                result.Add(new GpuAdapterInfo(d.Description, d.VendorId, LuidValue(d.Luid), (long)(nuint)d.DedicatedVideoMemory, (int)i));
            }
        }

        return result;
    }

    /// <summary>The GDI device names of the displays each adapter drives (<c>\\.\DISPLAY1</c>), keyed by adapter LUID.</summary>
    public static IReadOnlyDictionary<long, IReadOnlyList<string>> DisplaysByAdapter()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var result = new Dictionary<long, IReadOnlyList<string>>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            using (adapter)
            {
                var names = new List<string>();
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        names.Add(output.Description.DeviceName);
                    }
                }

                result[LuidValue(adapter.Description1.Luid)] = names;
            }
        }

        return result;
    }

    /// <summary>
    /// The adapter to capture and encode <paramref name="displayDeviceName"/> on: the one that drives it, so the desktop
    /// image never crosses between cards (ENGINE-NOTES.md §M.1); null when no adapter claims it.
    /// </summary>
    public static long? AdapterForDisplay(string displayDeviceName) =>
        DisplaysByAdapter().FirstOrDefault(kv => kv.Value.Contains(displayDeviceName, StringComparer.OrdinalIgnoreCase)) is { Value: not null } match
            ? match.Key
            : null;

    /// <summary>Creates the device on <paramref name="adapterLuid"/>, or on the first adapter when it is null or not found.</summary>
    public static VideoDevice Create(long? adapterLuid = null)
    {
        MediaFoundationRuntime.EnsureStarted();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        IDXGIAdapter1? chosen = null;
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            var d = adapter.Description1;
            if ((d.Flags & AdapterFlags.Software) == 0 && (adapterLuid is null || LuidValue(d.Luid) == adapterLuid))
            {
                chosen = adapter;
                break;
            }

            adapter.Dispose();
        }

        if (chosen is null && adapterLuid is not null)
        {
            return Create(null);
        }

        if (chosen is null)
        {
            throw new VideoCaptureException(VideoErrorCode.NoGraphicsDevice, "Video can't be recorded: Windows reports no graphics adapter. Audio recording is not affected. Update the graphics driver, then try again.");
        }

        using (chosen)
        {
            var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
            FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1];
            var result = D3D11.D3D11CreateDevice(chosen, DriverType.Unknown, flags, levels, out var device);
            if (result.Failure || device is null)
            {
                throw new VideoCaptureException(VideoErrorCode.NoGraphicsDevice, $"Video can't be recorded: the graphics adapter {chosen.Description1.Description} could not be opened (0x{result.Code:X8}). Audio recording is not affected. Update the graphics driver, then try again.");
            }

            using (var multithread = device.QueryInterface<ID3D11Multithread>())
            {
                multithread.SetMultithreadProtected(true);
            }

            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            var winRt = WinRtInterop.CreateDirect3DDevice(dxgiDevice.NativePointer);
            var manager = MediaFactory.MFCreateDXGIDeviceManager();
            manager.ResetDevice(device);
            var d = chosen.Description1;
            return new VideoDevice(device, winRt, manager, new GpuAdapterInfo(d.Description, d.VendorId, LuidValue(d.Luid), (long)(nuint)d.DedicatedVideoMemory, 0));
        }
    }

    internal static long LuidValue(Vortice.Luid luid) => ((long)luid.HighPart << 32) | luid.LowPart;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        VideoContext.Dispose();
        VideoDevice1.Dispose();
        DeviceManager.Dispose();
        (WinRtDevice as IDisposable)?.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
