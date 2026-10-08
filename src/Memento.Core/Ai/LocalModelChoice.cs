using Memento.Core.Engines;
using Memento.Core.Models;

namespace Memento.Core.Ai;

/// <summary>
/// The local language model to use (ARCHITECTURE.md §8, ENGINE-NOTES.md §H). The hardware recommendation is the
/// catalog's graphics-card model (Qwen3.5 4B) when the discrete card has its minimum video memory free, otherwise the
/// processor model (Ministral 3 3B). The model in effect is always an installed one when any is installed: the saved
/// choice, else the recommendation, else whichever local model is installed (the graphics-card one first when it fits),
/// so a PC with only one of the two models never reads as "not installed".
/// </summary>
public static class LocalModelChoice
{
    /// <summary>The recommended local model id for this hardware, or <c>null</c> when the catalog has none.</summary>
    public static string? RecommendedId(ModelCatalog catalog, ResourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        var models = catalog.OfKind(ModelKinds.Llm).ToList();
        var onGpu = models.FirstOrDefault(m => m.RecommendedFor == "gpu" && FitsOnGpu(m, snapshot));
        return (onGpu ?? models.FirstOrDefault(m => m.RecommendedFor == "cpu") ?? models.FirstOrDefault())?.Id;
    }

    /// <summary>
    /// The model in effect: the saved choice when it is an installed local model; otherwise the recommended one when it
    /// is installed; otherwise an installed local model (the graphics-card one when it fits, else the processor one,
    /// else the first); with none installed, the saved choice or the recommendation (the one to download).
    /// </summary>
    /// <param name="isInstalled">Whether a catalog id is installed and verified (<see cref="IModelManager.IsInstalled"/>).</param>
    public static string? EffectiveId(string? chosen, ModelCatalog catalog, ResourceSnapshot snapshot, Func<string, bool> isInstalled)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(isInstalled);
        var valid = chosen is not null && catalog.Find(chosen) is { Kind: ModelKinds.Llm };
        if (valid && isInstalled(chosen!))
        {
            return chosen;
        }

        var recommended = RecommendedId(catalog, snapshot);
        if (recommended is not null && isInstalled(recommended))
        {
            return recommended;
        }

        var installed = catalog.OfKind(ModelKinds.Llm).Where(m => isInstalled(m.Id)).ToList();
        if (installed.Count > 0)
        {
            return (installed.FirstOrDefault(m => m.RecommendedFor == "gpu" && FitsOnGpu(m, snapshot))
                ?? installed.FirstOrDefault(m => m.RecommendedFor == "cpu")
                ?? installed[0]).Id;
        }

        return valid ? chosen : recommended;
    }

    /// <summary>The discrete card has the model's minimum video memory free (its total when free is not known).</summary>
    public static bool FitsOnGpu(ModelCatalogEntry entry, ResourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(snapshot);
        var gpu = snapshot.DiscreteGpu;
        var free = gpu?.FreeVramBytes ?? gpu?.DedicatedVideoMemoryBytes;
        return free is { } f && entry.MinVramBytes is { } need && f >= need;
    }
}
