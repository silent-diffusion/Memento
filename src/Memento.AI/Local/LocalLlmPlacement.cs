namespace Memento.AI.Local;

/// <summary>
/// Where the host's <see cref="MemoryPlanner"/> placed a larger model (<see cref="LocalLlmJob.Placement"/>): the worker
/// loads exactly this, checks the RAM and video memory free again first (the job may have waited for the graphics card),
/// and when the card spills it moves more onto RAM (experts first, then blocks) rather than only shrinking the context.
/// </summary>
/// <param name="GpuLayers">llama.cpp's <c>n_gpu_layers</c>: blocks on the card plus the output layer.</param>
/// <param name="CpuExpertLayers">Mixture of experts: the first blocks whose routed experts stay in RAM (<c>--n-cpu-moe</c>).</param>
/// <param name="PlannedVramBytes">The plan's video memory, without the margin.</param>
/// <param name="PlannedRamBytes">The plan's RAM while writing.</param>
/// <param name="RamReserveBytes">RAM the plan keeps available for Windows, the app and what else runs.</param>
/// <param name="VramMarginBytes">Video memory kept free beyond <paramref name="PlannedVramBytes"/>.</param>
public sealed record LocalLlmPlacement(
    int GpuLayers,
    int CpuExpertLayers,
    long PlannedVramBytes,
    long PlannedRamBytes,
    long RamReserveBytes,
    long VramMarginBytes)
{
    /// <summary>The plan as a placement for the worker.</summary>
    public static LocalLlmPlacement From(LocalMemoryPlan plan, MemoryHeadroom headroom, long ramReserveBytes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(headroom);
        return new LocalLlmPlacement(plan.GpuLayers, plan.CpuExpertLayers, plan.VramBytes, plan.RamBytes, ramReserveBytes, headroom.VramMarginBytes);
    }

    /// <summary>
    /// The same load with less on the card, to make room for <paramref name="deficitBytes"/> more (a spill's shared growth,
    /// or what the card lacks now): a mixture of experts moves the experts of more blocks to RAM first, then blocks move to
    /// RAM. <c>null</c> when nothing worth having would be left on the card.
    /// </summary>
    public LocalLlmPlacement? LessOffload(LocalModelProfile profile, int contextTokens, long deficitBytes)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var layers = profile.Layers;
        var need = (long)(Math.Max(deficitBytes, 256L * 1024 * 1024) * 1.25);
        var perExperts = profile.ExpertBytesPerLayer;
        if (profile.IsMixtureOfExperts && perExperts > 0 && CpuExpertLayers < layers && GpuLayers > layers)
        {
            var more = (int)Math.Min(layers - CpuExpertLayers, (need + perExperts - 1) / perExperts);
            var moved = more * perExperts;
            return this with { CpuExpertLayers = CpuExpertLayers + more, PlannedVramBytes = Math.Max(0, PlannedVramBytes - moved), PlannedRamBytes = PlannedRamBytes + moved };
        }

        var blocks = Math.Min(layers, GpuLayers - 1);
        var expertsOnCard = Math.Max(0, blocks - CpuExpertLayers) > 0 ? perExperts : 0;
        var perBlock = Math.Max(1, profile.LayerBytes + expertsOnCard + (profile.KvBytesPerToken * contextTokens / Math.Max(1, layers)));
        var remove = (int)((need + perBlock - 1) / perBlock);
        var left = blocks - remove;
        if (left < 1)
        {
            return null;
        }

        var bytes = (long)remove * perBlock;
        return this with
        {
            GpuLayers = left + 1,
            CpuExpertLayers = profile.IsMixtureOfExperts ? layers : 0,
            PlannedVramBytes = Math.Max(0, PlannedVramBytes - bytes),
            PlannedRamBytes = PlannedRamBytes + bytes,
        };
    }
}
