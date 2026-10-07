using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Memento.Core.Engines.Interop;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Engines;

/// <summary>
/// <see cref="IResourceProbe"/> on Windows: GPUs and free video memory through DXGI (no <c>nvidia-smi</c>), processor
/// load from <c>GetSystemTimes</c>, memory from <c>GlobalMemoryStatusEx</c>. A failure of any part reads as "not
/// available" rather than an error. NVIDIA adapters are always discrete; AMD and Intel ones count as discrete when
/// they have at least 2 GB of their own memory (integrated GPUs report a small carve-out).
/// </summary>
public sealed partial class WindowsResourceProbe(ILogger<WindowsResourceProbe> logger) : IResourceProbe
{
    private const int NvidiaVendorId = 0x10DE;
    private const long DiscreteMemoryFloor = 2L * 1024 * 1024 * 1024;

    private readonly ILogger<WindowsResourceProbe> _logger = logger;
    private readonly object _gate = new();
    private (long Idle, long Total)? _lastTimes;
    private bool _dxgiFailureLogged;

    public ResourceSnapshot Sample()
    {
        if (!OperatingSystem.IsWindows())
        {
            return ResourceSnapshot.Empty;
        }

        var gpus = ReadGpus();
        var (total, available) = ReadMemory();
        return new ResourceSnapshot(gpus, ReadCpuBusy(), Environment.ProcessorCount, total, available);
    }

    /// <summary>Discrete adapters first, then by memory; software adapters (the Basic Render Driver) are left out.</summary>
    [SupportedOSPlatform("windows")]
    private List<GpuInfo> ReadGpus()
    {
        var result = new List<GpuInfo>();
        DxgiInterop.IDxgiFactory1? factory = null;
        try
        {
            var iid = DxgiInterop.FactoryIid;
            if (DxgiInterop.CreateDXGIFactory1(ref iid, out factory) < 0 || factory is null)
            {
                return result;
            }

            for (uint i = 0; i < 16; i++)
            {
                if (factory.EnumAdapters1(i, out var adapter) == DxgiInterop.DxgiErrorNotFound || adapter is null)
                {
                    break;
                }

                try
                {
                    if (adapter.GetDesc1(out var desc) < 0 || (desc.Flags & DxgiInterop.AdapterFlagSoftware) != 0)
                    {
                        continue;
                    }

                    long? free = null;
                    if (adapter.QueryVideoMemoryInfo(0, DxgiInterop.MemorySegmentGroupLocal, out var memory) >= 0)
                    {
                        free = (long)Math.Max(0, (double)memory.Budget - memory.CurrentUsage);
                    }

                    var dedicated = (long)desc.DedicatedVideoMemory;

                    // DXGI's budget is this process's: it stays high while another app holds the card's memory. What all
                    // processes use on the adapter bounds it (a model planned into "free" memory would spill otherwise).
                    if (PdhAdapterMemory.DedicatedUsage(desc.LuidLowPart, desc.LuidHighPart) is { } inUse && dedicated > 0)
                    {
                        var left = Math.Max(0, dedicated - inUse);
                        free = free is { } budget ? Math.Min(budget, left) : left;
                    }

                    var vendor = (int)desc.VendorId;
                    var discrete = vendor == NvidiaVendorId || dedicated >= DiscreteMemoryFloor;
                    result.Add(new GpuInfo((int)i, desc.Description.Trim(), vendor, dedicated, free, discrete));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            if (!_dxgiFailureLogged)
            {
                _dxgiFailureLogged = true;
                LogDxgiFailed(ex);
            }
        }
        finally
        {
            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }

        return result.OrderByDescending(g => g.IsDiscrete).ThenByDescending(g => g.DedicatedVideoMemoryBytes).ToList();
    }

    private double? ReadCpuBusy()
    {
        if (!SystemInterop.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return null;
        }

        // Kernel time includes idle time.
        var total = kernel + user;
        lock (_gate)
        {
            var previous = _lastTimes;
            _lastTimes = (idle, total);
            if (previous is not { } last || total <= last.Total)
            {
                return null;
            }

            var busy = 1.0 - ((double)(idle - last.Idle) / (total - last.Total));
            return Math.Clamp(busy * 100.0, 0, 100);
        }
    }

    private static (long Total, long Available) ReadMemory()
    {
        var status = new SystemInterop.MemoryStatusEx { Length = (uint)Marshal.SizeOf<SystemInterop.MemoryStatusEx>() };
        return SystemInterop.GlobalMemoryStatusEx(ref status) ? ((long)status.TotalPhys, (long)status.AvailPhys) : (0, 0);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DXGI could not list the graphics adapters; transcription will use the processor")]
    private partial void LogDxgiFailed(Exception exception);
}
