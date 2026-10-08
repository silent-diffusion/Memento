using System.Runtime.InteropServices;

namespace Memento.Core.Engines.Interop;

/// <summary>
/// Reads one wildcard performance counter (every instance's name and value) through PDH, with a query opened and closed
/// per read: the GPU counters' instances come and go with the processes that use the card. Raw counters such as
/// <c>Dedicated Usage</c> need a single collection.
/// </summary>
internal static class PdhCounters
{
    private const uint PdhFmtLarge = 0x00000400;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhNoData = 0x800007D5;
    private const uint PdhCstatusValidData = 0;
    private const uint PdhCstatusNewData = 1;

    /// <summary>The instances and values of <paramref name="englishPath"/>; empty when no instance exists, <c>null</c> when the counter cannot be read.</summary>
    public static List<GpuProcessCounter>? Read(string englishPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return ReadCore(englishPath);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static List<GpuProcessCounter>? ReadCore(string englishPath)
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out var query) != 0)
        {
            return null;
        }

        try
        {
            if (PdhAddEnglishCounterW(query, englishPath, IntPtr.Zero, out var counter) != 0)
            {
                return null;
            }

            var collected = PdhCollectQueryData(query);
            if (collected == PdhNoData)
            {
                return [];
            }

            if (collected != 0)
            {
                return null;
            }

            uint size = 0;
            var status = PdhGetFormattedCounterArrayW(counter, PdhFmtLarge, ref size, out var count, IntPtr.Zero);
            if (status != PdhMoreData)
            {
                // No instance matches the wildcard (nothing uses the card yet).
                return status == 0 || count == 0 ? [] : null;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, PdhFmtLarge, ref size, out count, buffer) != 0)
                {
                    return null;
                }

                var result = new List<GpuProcessCounter>((int)count);
                var itemSize = IntPtr.Size + 16;
                for (var i = 0; i < count; i++)
                {
                    // PDH_FMT_COUNTERVALUE_ITEM_W: szName (pointer), then PDH_FMT_COUNTERVALUE { CStatus (padded to 8), largeValue }.
                    var item = buffer + (i * itemSize);
                    var itemStatus = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                    if (itemStatus is PdhCstatusValidData or PdhCstatusNewData)
                    {
                        var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? string.Empty;
                        result.Add(new GpuProcessCounter(name, Marshal.ReadInt64(item, IntPtr.Size + 8)));
                    }
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = PdhCloseQuery(query);
        }
    }

#pragma warning disable SYSLIB1054 // Blittable arguments and a UTF-16 string; the built-in marshaller avoids unsafe code in Core.
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);
#pragma warning restore SYSLIB1054
}
