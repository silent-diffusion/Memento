namespace Memento.Video.Gpu;

/// <summary>A hardware graphics adapter as DXGI reports it.</summary>
/// <param name="Luid">The adapter's locally unique id, packed (high part in the upper 32 bits).</param>
/// <param name="Index">Its position in DXGI's enumeration (0 is the adapter driving the primary display).</param>
public sealed record GpuAdapterInfo(string Name, uint VendorId, long Luid, long DedicatedMemoryBytes, int Index)
{
    public const uint VendorNvidia = 0x10DE;
    public const uint VendorAmd = 0x1002;
    public const uint VendorIntel = 0x8086;
}
