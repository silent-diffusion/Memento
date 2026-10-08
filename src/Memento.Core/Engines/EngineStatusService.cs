using Memento.Core.Bridge.Contracts;
using Memento.Core.Models;
using Memento.Core.Settings;

namespace Memento.Core.Engines;

/// <summary>
/// <c>engine.status</c> and the footer's engine line: is each engine ready, where would it run, is it paused. The
/// transcription detail also carries the card's memory and who holds it, and says why the model runs on the processor
/// when the card is short (DESIGN.md §17).
/// </summary>
public sealed class EngineStatusService(EngineSelector selector, IModelManager models, ISettingsStore settings, ProcessingGate gate)
{
    /// <summary>The status from a fresh probe sample (who holds the card's memory may be a few seconds old).</summary>
    public EngineStatusResult Compute() => Compute(selector.Sample());

    /// <summary>The status with everything read again now (Settings' "Check again", <c>engine.refresh</c>).</summary>
    public EngineStatusResult Refresh() => Compute(selector.Refresh());

    /// <summary>The status for <paramref name="snapshot"/>.</summary>
    public EngineStatusResult Compute(ResourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var current = settings.Current;
        var paused = gate.Reason;
        var gpu = snapshot.DiscreteGpu;

        var modelId = selector.EffectiveModelId(current.Transcription, snapshot);
        var ready = models.IsInstalled(modelId);
        var device = selector.SelectDevice(modelId, forceCpu: false, snapshot);
        var transcription = new EngineStatusDetail(
            ready,
            ready ? device.Kind : null,
            gpu?.Name,
            gpu?.FreeVramBytes,
            modelId,
            paused)
        {
            GpuMemory = gpu is null ? null : GpuMemoryInfo.From(gpu),
            Note = ready ? ShortfallNote(modelId, device, gpu) : null,
        };

        var speakers = current.Speakers;
        var segmentation = models.Catalog.Entries.FirstOrDefault(e => e.Kind == ModelKinds.Speakers && e.Role == ModelRoles.Segmentation);
        var speakersReady = speakers.Identify
            && segmentation is not null && models.IsInstalled(segmentation.Id)
            && models.IsInstalled(speakers.EmbeddingModelId);
        var speakerDetail = new EngineStatusDetail(speakersReady, speakersReady ? "CPU" : null, null, null, speakers.EmbeddingModelId, paused);
        return new EngineStatusResult(transcription, speakerDetail);
    }

    /// <summary>"The graphics card has 0.8 GB of 6 GB free. … Large v3 Turbo needs 2.5 GB on the card, so it runs on the processor …".</summary>
    private string? ShortfallNote(string modelId, EngineDevice device, GpuInfo? gpu)
    {
        if (device.UseGpu || gpu is null || models.Catalog.Find(modelId) is not { MinVramBytes: { } needed } entry)
        {
            return null;
        }

        var free = gpu.FreeVramBytes ?? gpu.DedicatedVideoMemoryBytes;
        return free >= needed ? null : GpuMemoryWording.Shortfall(gpu, entry.Name, needed, "it transcribes on the processor (slower)");
    }
}
