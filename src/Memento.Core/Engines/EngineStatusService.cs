using Memento.Core.Bridge.Contracts;
using Memento.Core.Models;
using Memento.Core.Settings;

namespace Memento.Core.Engines;

/// <summary><c>engine.status</c> and the footer's engine line: is each engine ready, where would it run, is it paused.</summary>
public sealed class EngineStatusService(EngineSelector selector, IModelManager models, ISettingsStore settings, ProcessingGate gate)
{
    public EngineStatusResult Compute()
    {
        var snapshot = selector.Sample();
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
            paused);

        var speakers = current.Speakers;
        var segmentation = models.Catalog.Entries.FirstOrDefault(e => e.Kind == ModelKinds.Speakers && e.Role == ModelRoles.Segmentation);
        var speakersReady = speakers.Identify
            && segmentation is not null && models.IsInstalled(segmentation.Id)
            && models.IsInstalled(speakers.EmbeddingModelId);
        var speakerDetail = new EngineStatusDetail(speakersReady, speakersReady ? "CPU" : null, null, null, speakers.EmbeddingModelId, paused);
        return new EngineStatusResult(transcription, speakerDetail);
    }
}
