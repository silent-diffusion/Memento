using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Workers;

/// <summary>The job in a <c>start</c> command; exactly one of the bodies is set, as <see cref="Kind"/> says.</summary>
/// <param name="Llm">
/// A local language-model job (<see cref="WorkerJobKinds.Llm"/>): Memento.AI's <c>LocalLlmJob</c> as raw JSON, so Core
/// carries it without depending on the AI project. Its <c>device</c> says whether it may use the graphics card.
/// </param>
/// <param name="Live">2.0: the live transcript while recording (<see cref="WorkerJobKinds.Live"/>).</param>
public sealed record WorkerJob(string Kind, TranscribeJob? Transcribe = null, DiarizeJob? Diarize = null, JsonElement? Llm = null, LiveJob? Live = null)
{
    /// <summary>
    /// The job may use the graphics card: a transcription (full or live) with a runtime other than the CPU one, or a
    /// local model job whose device is not <c>cpu</c>. Such jobs run one at a time, so transcription and generation never
    /// share the card.
    /// </summary>
    [JsonIgnore]
    public bool UsesGpu =>
        (Transcribe is { } transcribe && transcribe.Runtimes.Any(r => !string.Equals(r, WorkerRuntimes.Cpu, StringComparison.Ordinal)))
        || (Live is { } live && live.Runtimes.Any(r => !string.Equals(r, WorkerRuntimes.Cpu, StringComparison.Ordinal)))
        || (Llm is { ValueKind: JsonValueKind.Object } llm
            && !(llm.TryGetProperty("device", out var device) && device.ValueKind == JsonValueKind.String && device.GetString() == WorkerRuntimes.Cpu));
}
