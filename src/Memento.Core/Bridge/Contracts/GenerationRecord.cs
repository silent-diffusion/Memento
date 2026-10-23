namespace Memento.Core.Bridge.Contracts;

/// <summary>How a document was made (BRIDGE.md M4): template, style, provider, inputs sent, and the checks per module.</summary>
/// <param name="PayloadHash">SHA-256 of the payload text, kept even when the text is not.</param>
/// <param name="PayloadKept">"Keep a record of what was sent" was on: <see cref="PayloadText"/> holds it.</param>
public sealed record GenerationRecord(
    string TemplateId,
    string TemplateName,
    string StyleId,
    string ProviderId,
    string ModelLabel,
    DateTimeOffset StartedAt,
    long DurationMs,
    InputSelection Inputs,
    string PayloadHash,
    bool PayloadKept,
    int Chunks,
    IReadOnlyList<GenerationModuleRecord> Modules)
{
    /// <summary>The included sections, as the preview names them.</summary>
    public IReadOnlyList<string> Sent { get; init; } = [];

    public long Bytes { get; init; }

    /// <summary>Nothing left the PC: the local model read it, or (2.5.0) "Your own server" on this PC did. Audio and video are never sent.</summary>
    public bool StayedOnPc { get; init; }

    public IReadOnlyList<GenerationClaim> Claims { get; init; } = [];

    public string? PayloadText { get; init; }

    /// <summary>After 2.2, a larger local model: where it ran, how fast and how much RAM it took; <c>null</c> otherwise.</summary>
    public string? Placement { get; init; }
}
