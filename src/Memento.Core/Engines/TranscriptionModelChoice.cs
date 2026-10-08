using Memento.Core.Models;

namespace Memento.Core.Engines;

/// <summary>
/// The transcription model to use (ARCHITECTURE.md §6). A model chosen in Settings is always the one in effect, installed
/// or not (the stage then waits for it and says so). Without a choice, the hardware recommendation (Large v3 Turbo when the
/// discrete card has its minimum video memory free, Small otherwise) is used when it is installed; otherwise the most
/// accurate installed model that fits the card, else the most accurate installed model (it then runs on the processor).
/// A PC with only Large v3 Turbo installed therefore never waits for Small while another program holds the card's memory.
/// </summary>
public static class TranscriptionModelChoice
{
    /// <summary>
    /// The model in effect: the saved choice when it names a transcription model; otherwise the recommended one when it
    /// is installed; otherwise an installed model (the first in catalog order that fits the card, else the first in
    /// catalog order); with none installed, the recommendation (the one to download).
    /// </summary>
    /// <param name="recommended">The hardware recommendation for this PC.</param>
    /// <param name="isInstalled">Whether a catalog id is installed and verified (<see cref="IModelManager.IsInstalled"/>).</param>
    public static string EffectiveId(string? chosen, string recommended, ModelCatalog catalog, ResourceSnapshot snapshot, Func<string, bool> isInstalled)
    {
        ArgumentNullException.ThrowIfNull(recommended);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(isInstalled);
        if (chosen is not null && catalog.Find(chosen) is { Kind: ModelKinds.Transcription })
        {
            return chosen;
        }

        if (isInstalled(recommended))
        {
            return recommended;
        }

        var installed = catalog.OfKind(ModelKinds.Transcription).Where(m => isInstalled(m.Id)).ToList();
        return installed.Count > 0
            ? (installed.FirstOrDefault(m => FitsOnGpu(m, snapshot)) ?? installed[0]).Id
            : recommended;
    }

    /// <summary>The discrete card has the model's minimum video memory free (its total when free is not known).</summary>
    public static bool FitsOnGpu(ModelCatalogEntry entry, ResourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(snapshot);
        var gpu = snapshot.DiscreteGpu;
        var free = gpu?.FreeVramBytes ?? gpu?.DedicatedVideoMemoryBytes;
        return free is { } f && f >= (entry.MinVramBytes ?? 0);
    }
}
