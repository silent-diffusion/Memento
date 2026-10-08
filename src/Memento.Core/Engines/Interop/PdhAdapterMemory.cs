using System.Globalization;

namespace Memento.Core.Engines.Interop;

/// <summary>
/// The video memory every process together uses on one adapter (<c>\GPU Adapter Memory(luid_…)\Dedicated Usage</c>), and
/// what each process uses there (<c>\GPU Process Memory(pid_N_luid_…)\Dedicated Usage</c>), read through PDH. DXGI's
/// budget is per process and stays high while another app holds the card's memory (ENGINE-NOTES §I.2: 5.3 GB "free" with
/// 4.3 GB held by another app), so the probe takes the smaller of the two.
/// </summary>
internal static class PdhAdapterMemory
{
    private const string ProcessMemoryPath = @"\GPU Process Memory(*)\Dedicated Usage";

    /// <summary>Bytes of dedicated video memory in use on the adapter, or <c>null</c> when the counter cannot be read.</summary>
    public static long? DedicatedUsage(uint luidLowPart, int luidHighPart)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $@"\GPU Adapter Memory(luid_0x{luidHighPart:x8}_0x{luidLowPart:x8}_phys_*)\Dedicated Usage");
        var instances = PdhCounters.Read(path);
        return instances is null or { Count: 0 } ? null : instances.Sum(i => i.Bytes);
    }

    /// <summary>Every process's dedicated video memory on every adapter, or <c>null</c> when the counter cannot be read.</summary>
    public static List<GpuProcessCounter>? ProcessUsage() => PdhCounters.Read(ProcessMemoryPath);
}
