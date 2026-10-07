namespace Memento.Core.Projects;

/// <summary>
/// How the next transcription pass should run, when it differs from Settings: chosen with
/// <c>transcript.retranscribe</c> or a remedy of <c>processing.retry</c>. Cleared when the pass finishes.
/// </summary>
/// <param name="ModelId">A transcription model other than the one in Settings.</param>
/// <param name="Language">A language other than the one in Settings.</param>
/// <param name="ForceCpu">Run on the processor (the <c>cpu</c> remedy).</param>
/// <param name="Retranscribe">The pass replaces an existing transcript (it is kept as a version when history is on).</param>
public sealed record ProcessingRequest(string? ModelId = null, string? Language = null, bool ForceCpu = false, bool Retranscribe = false);
