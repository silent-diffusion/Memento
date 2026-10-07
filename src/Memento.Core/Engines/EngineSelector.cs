using Memento.Core.Models;
using Memento.Core.Settings;

namespace Memento.Core.Engines;

/// <summary>
/// Picks the model and device (ARCHITECTURE.md §6): the default model is the most accurate one that fits this PC
/// (Large v3 Turbo on a discrete GPU with at least 2.5 GB free video memory, Small otherwise); a pass runs on the
/// discrete GPU when the model fits in its free memory and on the processor otherwise. A smaller model is offered as a
/// remedy, never applied silently.
/// </summary>
public sealed class EngineSelector(IModelManager models, IResourceProbe probe)
{
    public const string WhisperEngineName = "whisper.cpp";
    public const string GpuRecommendedModelId = "whisper-large-v3-turbo";
    public const string CpuRecommendedModelId = "whisper-small";

    public ModelCatalog Catalog => models.Catalog;

    public ResourceSnapshot Sample() => probe.Sample();

    /// <summary>The recommended transcription model for this PC.</summary>
    public string RecommendedTranscriptionModelId(ResourceSnapshot? snapshot = null)
    {
        var gpu = (snapshot ?? probe.Sample()).DiscreteGpu;
        var preferred = models.Catalog.OfKind(ModelKinds.Transcription).FirstOrDefault(e => e.RecommendedFor == "gpu");
        if (gpu is not null && preferred is not null && (gpu.FreeVramBytes ?? gpu.DedicatedVideoMemoryBytes) >= (preferred.MinVramBytes ?? 0))
        {
            return preferred.Id;
        }

        return models.Catalog.OfKind(ModelKinds.Transcription).FirstOrDefault(e => e.RecommendedFor == "cpu")?.Id ?? CpuRecommendedModelId;
    }

    /// <summary>Whether the catalog entry is the one to recommend on this PC.</summary>
    public bool IsRecommended(ModelCatalogEntry entry, ResourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Kind switch
        {
            ModelKinds.Transcription => entry.Id == RecommendedTranscriptionModelId(snapshot),
            ModelKinds.Llm => entry.Id == Ai.LocalModelChoice.RecommendedId(models.Catalog, snapshot),
            _ => entry.RecommendedFor == "any",
        };
    }

    /// <summary>The transcription model in effect: the saved choice, or the recommended one.</summary>
    public string EffectiveModelId(TranscriptionSettings settings, ResourceSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ModelId is { } chosen && models.Catalog.Find(chosen) is { Kind: ModelKinds.Transcription }
            ? chosen
            : RecommendedTranscriptionModelId(snapshot);
    }

    /// <summary>Where <paramref name="modelId"/> would run now.</summary>
    public EngineDevice SelectDevice(string modelId, bool forceCpu, ResourceSnapshot? snapshot = null)
    {
        var gpu = (snapshot ?? probe.Sample()).DiscreteGpu;
        if (forceCpu)
        {
            return new EngineDevice(false, null, "chosen: retry on the processor");
        }

        if (gpu is null)
        {
            return new EngineDevice(false, null, null);
        }

        var needed = models.Catalog.Find(modelId)?.MinVramBytes ?? 0;
        var free = gpu.FreeVramBytes ?? gpu.DedicatedVideoMemoryBytes;
        return free >= needed
            ? new EngineDevice(true, gpu, null)
            : new EngineDevice(false, null, $"{gpu.Name} has {Formatting.HumanFormat.Bytes(free)} of video memory free and the model needs {Formatting.HumanFormat.Bytes(needed)}");
    }
}
