using System.Globalization;
using System.Runtime.InteropServices;

namespace Memento.AI.Local;

/// <summary>
/// <see cref="IGpuProcessMemory"/> from the Windows performance counters Task Manager shows per process:
/// <c>\GPU Process Memory(pid_N_*)\Dedicated Usage</c> and <c>Shared Usage</c>, summed over this process's adapters.
/// Read through PDH directly (no System.Diagnostics.PerformanceCounter dependency). A query is opened per sample
/// because the instances appear only once the process has touched the GPU.
/// </summary>
public sealed partial class PdhGpuProcessMemory : IGpuProcessMemory
{
    private const uint PdhFmtLarge = 0x00000400;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhNoData = 0x800007D5;
    private const int ItemSize = 24;

    private readonly string _dedicatedPath;
    private readonly string _sharedPath;

    public PdhGpuProcessMemory()
        : this(Environment.ProcessId)
    {
    }

    public PdhGpuProcessMemory(int processId)
    {
        _dedicatedPath = string.Create(CultureInfo.InvariantCulture, $@"\GPU Process Memory(pid_{processId}_*)\Dedicated Usage");
        _sharedPath = string.Create(CultureInfo.InvariantCulture, $@"\GPU Process Memory(pid_{processId}_*)\Shared Usage");
    }

    public GpuProcessMemorySample? Sample()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (PdhOpenQueryW(null, IntPtr.Zero, out var query) != 0)
        {
            return null;
        }

        try
        {
            var addDedicated = PdhAddEnglishCounterW(query, _dedicatedPath, IntPtr.Zero, out var dedicated);
            var addShared = PdhAddEnglishCounterW(query, _sharedPath, IntPtr.Zero, out var shared);
            if (addDedicated != 0 || addShared != 0)
            {
                // No instance for this process yet: it has not touched the GPU, provided the counter set exists at all.
                return CounterSetExists() ? new GpuProcessMemorySample(0, 0) : null;
            }

            var collected = PdhCollectQueryData(query);
            if (collected == PdhNoData)
            {
                // No instance matches yet: this process has not allocated GPU memory.
                return new GpuProcessMemorySample(0, 0);
            }

            if (collected != 0)
            {
                return null;
            }

            var dedicatedBytes = Sum(dedicated);
            var sharedBytes = Sum(shared);
            return dedicatedBytes is null || sharedBytes is null ? null : new GpuProcessMemorySample(dedicatedBytes.Value, sharedBytes.Value);
        }
        finally
        {
            _ = PdhCloseQuery(query);
        }
    }

    private static bool CounterSetExists()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out var query) != 0)
        {
            return false;
        }

        try
        {
            return PdhAddEnglishCounterW(query, @"\GPU Process Memory(*)\Shared Usage", IntPtr.Zero, out _) == 0;
        }
        finally
        {
            _ = PdhCloseQuery(query);
        }
    }

    private static long? Sum(IntPtr counter)
    {
        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, PdhFmtLarge, ref size, out var count, IntPtr.Zero);
        if (status != PdhMoreData)
        {
            // No instance yet (the process has not used the GPU): nothing allocated.
            return status == 0 ? 0 : count == 0 ? 0 : null;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PdhFmtLarge, ref size, out count, buffer) != 0)
            {
                return null;
            }

            long total = 0;
            for (var i = 0; i < count; i++)
            {
                // PDH_FMT_COUNTERVALUE_ITEM_W: szName (pointer), then PDH_FMT_COUNTERVALUE { CStatus (uint, padded to 8), largeValue (long) }.
                var item = buffer + (i * ItemSize);
                var itemStatus = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                if (itemStatus is 0 or 1)
                {
                    total += Marshal.ReadInt64(item, IntPtr.Size + 8);
                }
            }

            return total;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(IntPtr query);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(IntPtr query);
}
