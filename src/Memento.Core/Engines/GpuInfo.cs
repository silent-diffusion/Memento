namespace Memento.Core.Engines;

/// <summary>One graphics adapter as DXGI reports it.</summary>
/// <param name="AdapterIndex">DXGI enumeration order (0 is the one Windows uses for the desktop).</param>
/// <param name="DedicatedVideoMemoryBytes">Memory on the card (integrated GPUs report a small carve-out).</param>
/// <param name="FreeVramBytes">Budget minus current use of the local memory segment; <c>null</c> when it could not be read.</param>
/// <param name="IsDiscrete">A separate graphics card rather than one built into the processor.</param>
public sealed record GpuInfo(
    int AdapterIndex,
    string Name,
    int VendorId,
    long DedicatedVideoMemoryBytes,
    long? FreeVramBytes,
    bool IsDiscrete);
