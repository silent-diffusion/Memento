using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Memento.Core.Engines.Interop;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Engines;

/// <summary>
/// <see cref="IResourceProbe"/> on Windows: GPUs and free video memory through DXGI and the PDH GPU counters (no
/// <c>nvidia-smi</c>), who holds a discrete card's memory (<see cref="GpuMemoryAttribution"/>, reused for
/// <see cref="HolderCacheMilliseconds"/>), processor load from <c>GetSystemTimes</c>, memory from
/// <c>GlobalMemoryStatusEx</c>. A failure of any part reads as "not available" rather than an error. The display driver's
/// hybrid flags decide which adapter is the separate card on a laptop; without them NVIDIA adapters are discrete and AMD
/// and Intel ones count as discrete when they have at least 2 GB of their own memory (integrated GPUs report a small
/// carve-out).
/// </summary>
public sealed partial class WindowsResourceProbe(ILogger<WindowsResourceProbe> logger) : IResourceProbe
{
    /// <summary>How long a reading of who holds the card's memory is reused (the footer samples every 5 seconds).</summary>
    public const int HolderCacheMilliseconds = 4_000;

    private const int NvidiaVendorId = 0x10DE;
    private const long DiscreteMemoryFloor = 2L * 1024 * 1024 * 1024;

    private readonly ILogger<WindowsResourceProbe> _logger = logger;
    private readonly object _gate = new();
    private (long Idle, long Total)? _lastTimes;
    private (long At, Dictionary<long, IReadOnlyList<GpuMemoryHolder>> ByLuid)? _holders;
    private bool _dxgiFailureLogged;
    private bool _holdersFailureLogged;

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

    /// <summary>Reads everything again now, including who holds the card's memory (Settings' "Check again").</summary>
    public ResourceSnapshot Refresh()
    {
        lock (_gate)
        {
            _holders = null;
        }

        return Sample();
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
                    var inUse = PdhAdapterMemory.DedicatedUsage(desc.LuidLowPart, desc.LuidHighPart);
                    if (inUse is { } used && dedicated > 0)
                    {
                        var left = Math.Max(0, dedicated - used);
                        free = free is { } budget ? Math.Min(budget, left) : left;
                    }

                    // The driver's hybrid flags first: an APU whose firmware reserves 2 GB or more is still integrated.
                    var vendor = (int)desc.VendorId;
                    var discrete = D3dkmtAdapterType.IsHybridDiscrete(desc.LuidLowPart, desc.LuidHighPart)
                        ?? (vendor == NvidiaVendorId || dedicated >= DiscreteMemoryFloor);
                    var luid = GpuMemoryAttribution.Luid(desc.LuidLowPart, desc.LuidHighPart);
                    result.Add(new GpuInfo((int)i, desc.Description.Trim(), vendor, dedicated, free, discrete) { Luid = luid, UsedBytes = inUse });
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

        return WithHolders(result).OrderByDescending(g => g.IsDiscrete).ThenByDescending(g => g.DedicatedVideoMemoryBytes).ToList();
    }

    /// <summary>Discrete cards get who holds their memory; the reading is reused for a few seconds and never throws.</summary>
    private List<GpuInfo> WithHolders(List<GpuInfo> gpus)
    {
        var luids = gpus.Where(g => g.IsDiscrete && g.Luid is not null).Select(g => g.Luid!.Value).ToList();
        if (luids.Count == 0)
        {
            return gpus;
        }

        Dictionary<long, IReadOnlyList<GpuMemoryHolder>>? byLuid = null;
        lock (_gate)
        {
            if (_holders is { } cached && Environment.TickCount64 - cached.At < HolderCacheMilliseconds && luids.All(cached.ByLuid.ContainsKey))
            {
                byLuid = cached.ByLuid;
            }
        }

        if (byLuid is null)
        {
            // Read outside the lock so a processor-load sample never waits on PDH.
            byLuid = ReadHolders(luids);
            lock (_gate)
            {
                _holders = (Environment.TickCount64, byLuid);
            }
        }

        return gpus.Select(g => g.Luid is { } luid && byLuid.TryGetValue(luid, out var holders) ? g with { Holders = holders } : g).ToList();
    }

    private Dictionary<long, IReadOnlyList<GpuMemoryHolder>> ReadHolders(List<long> luids)
    {
        var result = new Dictionary<long, IReadOnlyList<GpuMemoryHolder>>();
        try
        {
            var counters = PdhAdapterMemory.ProcessUsage();
            var processes = counters is null ? null : WindowsProcessDirectory.Take();
            foreach (var luid in luids)
            {
                result[luid] = counters is null || processes is null
                    ? []
                    : GpuMemoryAttribution.Holders(GpuMemoryAttribution.ByProcess(counters, luid), processes, Environment.ProcessId);
            }
        }
#pragma warning disable CA1031 // Naming who holds the card is a courtesy: any failure reads as "not known", never as an error.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (!_holdersFailureLogged)
            {
                _holdersFailureLogged = true;
                LogHoldersFailed(ex);
            }

            foreach (var luid in luids)
            {
                result[luid] = [];
            }
        }

        return result;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read which programs hold the graphics card's memory; Settings will not name them")]
    private partial void LogHoldersFailed(Exception exception);
}
