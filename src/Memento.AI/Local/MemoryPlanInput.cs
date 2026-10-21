using Memento.Core.Engines;

namespace Memento.AI.Local;

/// <summary>What <see cref="MemoryPlanner"/> plans with, read just before planning.</summary>
/// <param name="FreeVramBytes">The discrete card's free memory (other programs' use already taken off); <c>null</c> without a card or a reading.</param>
/// <param name="AvailableRamBytes">RAM available now (<c>GlobalMemoryStatusEx</c>'s <c>ullAvailPhys</c>: free and standby pages).</param>
/// <param name="TotalRamBytes">Installed RAM Windows can use.</param>
/// <param name="LogicalProcessors">Processor threads.</param>
/// <param name="Demands">What else in Memento needs memory now.</param>
public sealed record MemoryPlanInput(long? FreeVramBytes, long AvailableRamBytes, long TotalRamBytes, int LogicalProcessors, MemoryDemands Demands)
{
    /// <summary>From a resource sample.</summary>
    public static MemoryPlanInput From(ResourceSnapshot snapshot, MemoryDemands demands)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(demands);
        return new MemoryPlanInput(snapshot.DiscreteGpu?.FreeVramBytes, snapshot.AvailableRamBytes, snapshot.TotalRamBytes, snapshot.LogicalProcessors, demands);
    }
}
