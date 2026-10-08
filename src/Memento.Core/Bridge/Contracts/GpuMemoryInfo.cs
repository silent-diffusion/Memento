using Memento.Core.Engines;

namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// The discrete graphics card's memory as the probe read it (<c>engine.status</c>, <c>engine.refresh</c>,
/// <c>providers.list</c>): what is free, what every process together uses, and who holds the most.
/// </summary>
/// <param name="GpuName">"NVIDIA GeForce RTX 3060 Laptop GPU".</param>
/// <param name="TotalBytes">Dedicated memory on the card as DXGI reports it (a little under the size it is sold with).</param>
/// <param name="FreeBytes">Free now: the smaller of the DXGI budget and the card minus what every process uses; <c>null</c> when unreadable.</param>
/// <param name="UsedBytes">What every process together uses on the card (PDH); <c>null</c> when unreadable.</param>
/// <param name="Holders">Up to three programs holding the most, largest first; empty when none holds 32 MB or the counters cannot be read.</param>
/// <param name="Summary">"The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB."</param>
public sealed record GpuMemoryInfo(string GpuName, long TotalBytes, long? FreeBytes, long? UsedBytes, IReadOnlyList<GpuMemoryHolderInfo> Holders, string Summary)
{
    /// <summary>The bridge view of a probed card.</summary>
    public static GpuMemoryInfo From(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        return new GpuMemoryInfo(
            gpu.Name,
            gpu.DedicatedVideoMemoryBytes,
            gpu.FreeVramBytes,
            gpu.UsedBytes,
            gpu.Holders.Select(h => new GpuMemoryHolderInfo(h.ProcessName, h.Description, h.Bytes, h.IsMemento, h.StartedBy)).ToList(),
            GpuMemoryWording.Summary(gpu));
    }
}
