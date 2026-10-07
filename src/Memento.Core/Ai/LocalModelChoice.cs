using Memento.Core.Engines;
using Memento.Core.Models;

namespace Memento.Core.Ai;

/// <summary>
/// The local language model to use when Settings has no choice (ARCHITECTURE.md §8, ENGINE-NOTES.md §H): the catalog's
/// graphics-card model (Qwen3.5 4B) when the discrete card has its minimum video memory free, otherwise the processor
/// model (Ministral 3 3B).
/// </summary>
public static class LocalModelChoice
{
    /// <summary>The recommended local model id, or <c>null</c> when the catalog has none.</summary>
    public static string? RecommendedId(ModelCatalog catalog, ResourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        var models = catalog.OfKind(ModelKinds.Llm).ToList();
        var gpu = snapshot.DiscreteGpu;
        var free = gpu?.FreeVramBytes ?? gpu?.DedicatedVideoMemoryBytes;
        var onGpu = models.FirstOrDefault(m => m.RecommendedFor == "gpu" && free is { } f && m.MinVramBytes is { } need && f >= need);
        return (onGpu ?? models.FirstOrDefault(m => m.RecommendedFor == "cpu") ?? models.FirstOrDefault())?.Id;
    }

    /// <summary>The model in effect: the saved choice when it is a local model in the catalog, otherwise the recommended one.</summary>
    public static string? EffectiveId(string? chosen, ModelCatalog catalog, ResourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return chosen is not null && catalog.Find(chosen) is { Kind: ModelKinds.Llm } ? chosen : RecommendedId(catalog, snapshot);
    }
}
