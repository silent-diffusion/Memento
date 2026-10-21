using Memento.Core.Processing;
using Memento.Core.Recording;
using Memento.Core.Settings;

namespace Memento.Core.Engines;

/// <summary>
/// <see cref="IMemoryDemands"/> from the running app: the recording coordinator (capturing, with video) and the
/// processing orchestrator (a transcription queued, waiting or running) with the transcription model in effect, on the
/// card when it fits there (its minimum video memory) and on the processor otherwise (its file plus working memory).
/// </summary>
public sealed class AppMemoryDemands(RecordingCoordinator recordings, ProcessingOrchestrator processing, EngineSelector engines, ISettingsStore settings) : IMemoryDemands
{
    /// <summary>Whisper's own working memory beside its weights (buffers, the audio window): measured 0.3–0.5 GB (ENGINE-NOTES.md §T).</summary>
    public const long WhisperWorkingBytes = 512L * 1024 * 1024;

    public MemoryDemands Current()
    {
        var pending = processing.IsProcessing;
        long vram = 0;
        long ram = 0;
        if (pending)
        {
            var snapshot = engines.Sample();
            var modelId = engines.EffectiveModelId(settings.Current.Transcription, snapshot);
            var entry = engines.Catalog.Find(modelId);
            var device = engines.SelectDevice(modelId, false, snapshot);
            vram = device.UseGpu ? entry?.MinVramBytes ?? 0 : 0;
            ram = WhisperWorkingBytes + (device.UseGpu ? 0 : entry?.SizeBytes ?? 0);
        }

        return new MemoryDemands(recordings.IsCapturing, recordings.IsCapturingVideo, pending, vram, ram);
    }
}
