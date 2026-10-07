using Memento.Core.Engines;

namespace Memento.Core.Tests.Fakes;

/// <summary>A PC under test control: set <see cref="Snapshot"/>.</summary>
internal sealed class FakeResourceProbe : IResourceProbe
{
    public static readonly GpuInfo Rtx = new(0, "NVIDIA GeForce RTX 3060 Laptop GPU", 0x10DE, 6L << 30, 5L << 30, IsDiscrete: true);

    public ResourceSnapshot Snapshot { get; set; } = ResourceSnapshot.Empty;

    public ResourceSnapshot Sample() => Snapshot;

    public static ResourceSnapshot WithGpu(long freeVramBytes) =>
        ResourceSnapshot.Empty with { Gpus = [Rtx with { FreeVramBytes = freeVramBytes }] };
}
