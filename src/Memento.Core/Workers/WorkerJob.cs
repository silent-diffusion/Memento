using System.Text.Json.Serialization;

namespace Memento.Core.Workers;

/// <summary>The job in a <c>start</c> command; exactly one of the bodies is set, as <see cref="Kind"/> says.</summary>
public sealed record WorkerJob(string Kind, TranscribeJob? Transcribe = null, DiarizeJob? Diarize = null)
{
    /// <summary>The job may use the graphics card (a transcription with a runtime other than the CPU one).</summary>
    [JsonIgnore]
    public bool UsesGpu => Transcribe is { } transcribe && transcribe.Runtimes.Any(r => !string.Equals(r, WorkerRuntimes.Cpu, StringComparison.Ordinal));
}
