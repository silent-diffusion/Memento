namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>generation.progress</c> (throttled; final states always sent).</summary>
/// <param name="Stage"><c>waiting</c>, <c>composing</c>, <c>generating</c>, <c>verifying</c>, <c>rendering</c>, <c>done</c>, <c>failed</c> or <c>cancelled</c>.</param>
/// <param name="DocumentId">The document written (on <c>done</c>), or the one being regenerated.</param>
/// <param name="Message">What is happening, or the specific failure (DESIGN.md §17).</param>
public sealed record GenerationProgress(string JobId, string RecordingId, string? DocumentId, string Stage, string? ModuleId, double Percent, string? Message)
{
    /// <summary>On <c>failed</c>: the error code (<c>ai.network</c>, <c>ai.notEnoughVram</c>…).</summary>
    public string? Code { get; init; }
}
