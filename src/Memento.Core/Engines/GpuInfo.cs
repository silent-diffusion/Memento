namespace Memento.Core.Engines;

/// <summary>One graphics adapter as DXGI reports it.</summary>
/// <param name="AdapterIndex">DXGI enumeration order (0 is the one Windows uses for the desktop).</param>
/// <param name="DedicatedVideoMemoryBytes">Memory on the card (integrated GPUs report a small carve-out).</param>
/// <param name="FreeVramBytes">
/// What is free on the card now: the smaller of this process's DXGI budget minus its use and the card's memory minus what
/// every process together uses on it (PDH); <c>null</c> when it could not be read.
/// </param>
/// <param name="IsDiscrete">A separate graphics card rather than one built into the processor.</param>
public sealed record GpuInfo(
    int AdapterIndex,
    string Name,
    int VendorId,
    long DedicatedVideoMemoryBytes,
    long? FreeVramBytes,
    bool IsDiscrete)
{
    /// <summary>The adapter's locally unique id (high part in the upper 32 bits), which the performance counters name; <c>null</c> when unknown.</summary>
    public long? Luid { get; init; }

    /// <summary>Dedicated video memory every process together uses on the card (PDH); <c>null</c> when the counter cannot be read.</summary>
    public long? UsedBytes { get; init; }

    /// <summary>The programs holding the most of the card's memory, largest first (at most three; Memento's own processes are one entry).</summary>
    public IReadOnlyList<GpuMemoryHolder> Holders { get; init; } = [];
}
