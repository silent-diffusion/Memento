using System.Globalization;
using System.Runtime.InteropServices;

namespace Memento.Core.Engines.Interop;

/// <summary>
/// The video memory every process together uses on one adapter (<c>\GPU Adapter Memory(luid_…)\Dedicated Usage</c>),
/// read through PDH. DXGI's budget is per process and stays high while another app holds the card's memory (ENGINE-NOTES
/// §I.2: 5.3 GB "free" with 4.3 GB held by another app), so the probe takes the smaller of the two.
/// </summary>
internal static class PdhAdapterMemory
{
    private const uint PdhFmtLarge = 0x00000400;
    private const uint PdhMoreData = 0x800007D2;

    /// <summary>Bytes of dedicated video memory in use on the adapter, or <c>null</c> when the counter cannot be read.</summary>
    public static long? DedicatedUsage(uint luidLowPart, int luidHighPart)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $@"\GPU Adapter Memory(luid_0x{luidHighPart:x8}_0x{luidLowPart:x8}_phys_*)\Dedicated Usage");
        if (PdhOpenQueryW(null, IntPtr.Zero, out var query) != 0)
        {
            return null;
        }

        try
        {
            if (PdhAddEnglishCounterW(query, path, IntPtr.Zero, out var counter) != 0 || PdhCollectQueryData(query) != 0)
            {
                return null;
            }

            uint size = 0;
            if (PdhGetFormattedCounterArrayW(counter, PdhFmtLarge, ref size, out var count, IntPtr.Zero) != PdhMoreData || count == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArrayW(counter, PdhFmtLarge, ref size, out count, buffer) != 0)
                {
                    return null;
                }

                long total = 0;
                var itemSize = IntPtr.Size + 16;
                for (var i = 0; i < count; i++)
                {
                    // PDH_FMT_COUNTERVALUE_ITEM_W: szName (pointer), then PDH_FMT_COUNTERVALUE { CStatus (padded to 8), largeValue }.
                    var item = buffer + (i * itemSize);
                    var status = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                    if (status is 0 or 1)
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
